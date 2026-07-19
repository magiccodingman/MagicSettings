namespace MagicSettings.Server;

public sealed class MagicNodeProofVerifier
{
    private readonly IMagicCredentialRegistry _credentials;
    private readonly IMagicReplayCache _replayCache;
    private readonly TimeSpan _allowedClockSkew;

    public MagicNodeProofVerifier(IMagicCredentialRegistry credentials, IMagicReplayCache replayCache, TimeSpan? allowedClockSkew = null)
    {
        _credentials = credentials;
        _replayCache = replayCache;
        _allowedClockSkew = allowedClockSkew ?? TimeSpan.FromSeconds(30);
    }

    public async ValueTask<MagicProofVerificationResult> VerifyAsync(MagicProofVerificationRequest request, CancellationToken cancellationToken = default)
    {
        var structural = ValidateRequest(request);
        if (!structural.IsValid)
        {
            return structural;
        }

        var proof = request.Proof;
        var credential = await _credentials.FindAsync(proof.NodeId, proof.CredentialId, cancellationToken);
        if (credential is null)
        {
            return MagicProofVerificationResult.Invalid("The credential is unknown.");
        }

        if (credential.Status is not (MagicCredentialStatus.Approved or MagicCredentialStatus.Retiring))
        {
            return MagicProofVerificationResult.Invalid($"The credential is {credential.Status}.");
        }

        return await VerifySignatureAndReplayAsync(credential.PublicKey, request, cancellationToken);
    }

    public async ValueTask<MagicProofVerificationResult> VerifyEnrollmentAsync(
        MagicNodeIdentityDescriptor identity,
        MagicProofVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var structural = ValidateRequest(request);
        if (!structural.IsValid)
        {
            return structural;
        }

        if (request.Proof.NodeId != identity.NodeId || request.Proof.CredentialId != identity.CredentialId)
        {
            return MagicProofVerificationResult.Invalid("The proof does not match the supplied node identity.");
        }

        if (identity.CredentialKind != MagicCredentialKind.EcdsaP256
            || !string.Equals(identity.SignatureAlgorithm, "ECDSA_P256_SHA256_P1363", StringComparison.Ordinal))
        {
            return MagicProofVerificationResult.Invalid("The supplied credential algorithm is unsupported.");
        }

        string fingerprint;
        try
        {
            fingerprint = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(identity.PublicKey))).ToLowerInvariant();
        }
        catch (FormatException)
        {
            return MagicProofVerificationResult.Invalid("The supplied public key is malformed.");
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(fingerprint),
                Encoding.ASCII.GetBytes(identity.Fingerprint.ToLowerInvariant())))
        {
            return MagicProofVerificationResult.Invalid("The supplied public-key fingerprint is invalid.");
        }

        return await VerifySignatureAndReplayAsync(identity.PublicKey, request, cancellationToken);
    }

    public static bool VerifyContinuity(MagicIdentityContinuityProof proof)
    {
        var canonical = string.Join("\n",
            "MAGICSETTINGS-IDENTITY-CONTINUITY-V1",
            proof.PreviousIdentity.NodeId.ToString("D"),
            proof.PreviousIdentity.CredentialId.ToString("D"),
            proof.NewIdentity.CredentialId.ToString("D"),
            proof.NewIdentity.PublicKey,
            proof.IssuedUtc.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            proof.Nonce);
        return VerifySignature(proof.PreviousIdentity.PublicKey, canonical, proof.Signature)
               && proof.PreviousIdentity.NodeId == proof.NewIdentity.NodeId;
    }

    private MagicProofVerificationResult ValidateRequest(MagicProofVerificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var proof = request.Proof;
        if (!string.Equals(proof.Version, "MAGICSETTINGS-PROOF-V1", StringComparison.Ordinal))
            return MagicProofVerificationResult.Invalid("Unsupported proof version.");
        if (!string.Equals(proof.Audience, request.ExpectedAudience, StringComparison.Ordinal))
            return MagicProofVerificationResult.Invalid("The proof audience does not match this API.");
        if (!string.Equals(proof.Method, request.Method, StringComparison.OrdinalIgnoreCase))
            return MagicProofVerificationResult.Invalid("The HTTP method does not match the signed proof.");
        if (!string.Equals(proof.Target, NormalizeTarget(request.Uri), StringComparison.Ordinal))
            return MagicProofVerificationResult.Invalid("The request target does not match the signed proof.");
        if (!string.Equals(proof.BodySha256, request.BodySha256, StringComparison.OrdinalIgnoreCase))
            return MagicProofVerificationResult.Invalid("The request body hash does not match the signed proof.");
        if (proof.IssuedUtc - _allowedClockSkew > request.NowUtc)
            return MagicProofVerificationResult.Invalid("The proof was issued in the future.");
        if (proof.ExpiresUtc + _allowedClockSkew < request.NowUtc)
            return MagicProofVerificationResult.Invalid("The proof has expired.");
        if (proof.ExpiresUtc <= proof.IssuedUtc || proof.ExpiresUtc - proof.IssuedUtc > TimeSpan.FromMinutes(5))
            return MagicProofVerificationResult.Invalid("The proof lifetime is invalid.");
        if (string.IsNullOrWhiteSpace(proof.Nonce))
            return MagicProofVerificationResult.Invalid("The proof nonce is missing.");
        return MagicProofVerificationResult.Valid;
    }

    private async ValueTask<MagicProofVerificationResult> VerifySignatureAndReplayAsync(
        string publicKey,
        MagicProofVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var proof = request.Proof;
        if (!VerifySignature(publicKey, Canonicalize(proof), proof.Signature))
        {
            return MagicProofVerificationResult.Invalid("The proof signature is invalid.");
        }

        if (!await _replayCache.TryUseAsync(proof.CredentialId, proof.Nonce, proof.ExpiresUtc, cancellationToken))
        {
            return MagicProofVerificationResult.Invalid("The proof nonce has already been used.");
        }

        return MagicProofVerificationResult.Valid;
    }

    private static bool VerifySignature(string publicKey, string canonical, string signature)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return key.VerifyData(
                Encoding.UTF8.GetBytes(canonical),
                Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Canonicalize(MagicAuthenticationProof proof)
        => string.Join("\n",
            proof.Version,
            proof.NodeId.ToString("D"),
            proof.CredentialId.ToString("D"),
            proof.Audience,
            proof.Method.ToUpperInvariant(),
            proof.Target,
            proof.BodySha256.ToLowerInvariant(),
            proof.IssuedUtc.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            proof.ExpiresUtc.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            proof.Nonce);

    private static string NormalizeTarget(Uri uri)
        => uri.IsAbsoluteUri
            ? uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped)
            : uri.OriginalString;
}
