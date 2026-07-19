namespace MagicSettings;

internal static class MagicIdentityCryptography
{
    public const string ProofVersion = "MAGICSETTINGS-PROOF-V1";
    public const string SignatureAlgorithm = "ECDSA_P256_SHA256_P1363";

    public static MagicStoredNodeIdentity Create(Guid? nodeId = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new(
            nodeId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
            Convert.ToBase64String(key.ExportPkcs8PrivateKey()),
            DateTimeOffset.UtcNow);
    }

    public static MagicNodeIdentityDescriptor Describe(MagicStoredNodeIdentity identity)
        => new(
            identity.NodeId,
            identity.CredentialId,
            MagicCredentialKind.EcdsaP256,
            SignatureAlgorithm,
            identity.PublicKey,
            Fingerprint(identity.PublicKey),
            identity.CreatedUtc);

    public static string Sign(MagicStoredNodeIdentity identity, string canonical)
    {
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(identity.PrivateKey), out _);
        var signature = key.SignData(
            Encoding.UTF8.GetBytes(canonical),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Convert.ToBase64String(signature);
    }

    public static bool Verify(string publicKey, string canonical, string signature)
    {
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
        return key.VerifyData(
            Encoding.UTF8.GetBytes(canonical),
            Convert.FromBase64String(signature),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    public static string Canonicalize(MagicAuthenticationProof proof)
        => string.Join('\n',
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

    public static string CanonicalizeContinuity(MagicNodeIdentityDescriptor previous, MagicNodeIdentityDescriptor current, DateTimeOffset issuedUtc, string nonce)
        => string.Join('\n',
            "MAGICSETTINGS-IDENTITY-CONTINUITY-V1",
            previous.NodeId.ToString("D"),
            previous.CredentialId.ToString("D"),
            current.CredentialId.ToString("D"),
            current.PublicKey,
            issuedUtc.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            nonce);

    public static string NormalizeTarget(Uri uri)
        => uri.IsAbsoluteUri
            ? uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped)
            : uri.OriginalString;

    public static string Fingerprint(string publicKey)
        => Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(publicKey))).ToLowerInvariant();
}
