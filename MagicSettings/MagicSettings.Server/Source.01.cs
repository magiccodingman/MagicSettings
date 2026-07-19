// Consolidated source file. See repository history and wiki for subsystem boundaries.
using System.Security.Cryptography;
using MagicSettings.Share;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Text;

namespace MagicSettings.Server
{
public static class MagicAspNetProofVerificationExtensions
{
    public static async ValueTask<MagicProofVerificationResult> VerifyHttpRequestAsync(
        this MagicNodeProofVerifier verifier,
        HttpRequest request,
        string expectedAudience,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAudience);

        if (!request.Headers.TryGetValue("Authorization", out var authorization))
        {
            return MagicProofVerificationResult.Invalid("The MagicNode authorization header is missing.");
        }

        var header = authorization.ToString();
        const string prefix = "MagicNode ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return MagicProofVerificationResult.Invalid("The authorization scheme is not MagicNode.");
        }

        MagicAuthenticationProof proof;
        try
        {
            proof = MagicNodeProofCodec.Decode(header[prefix.Length..].Trim());
        }
        catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException)
        {
            return MagicProofVerificationResult.Invalid("The MagicNode authorization proof is malformed.");
        }

        request.EnableBuffering();
        string bodyHash;
        if (request.ContentLength is null or 0)
        {
            bodyHash = Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant();
        }
        else
        {
            await using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, cancellationToken);
            request.Body.Position = 0;
            bodyHash = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
        }

        var uri = new Uri($"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}");
        return await verifier.VerifyAsync(
            new(proof, expectedAudience, request.Method, uri, bodyHash, DateTimeOffset.UtcNow),
            cancellationToken);
    }
}
}
namespace MagicSettings.Server
{
/// <summary>
/// Storage-agnostic credential lifecycle helpers for control-plane implementations.
/// </summary>
public sealed class MagicCredentialAdministrationService
{
    private readonly IMagicCredentialRegistry _credentials;

    public MagicCredentialAdministrationService(IMagicCredentialRegistry credentials) => _credentials = credentials;

    public async ValueTask<bool> SetStatusAsync(
        Guid nodeId,
        Guid credentialId,
        MagicCredentialStatus status,
        CancellationToken cancellationToken = default)
    {
        var credential = await _credentials.FindAsync(nodeId, credentialId, cancellationToken);
        if (credential is null)
        {
            return false;
        }

        await _credentials.UpsertAsync(credential with
        {
            Status = status,
            UpdatedUtc = DateTimeOffset.UtcNow
        }, cancellationToken);
        return true;
    }

    public ValueTask<bool> ApproveAsync(Guid nodeId, Guid credentialId, CancellationToken cancellationToken = default)
        => SetStatusAsync(nodeId, credentialId, MagicCredentialStatus.Approved, cancellationToken);

    public ValueTask<bool> RevokeAsync(Guid nodeId, Guid credentialId, CancellationToken cancellationToken = default)
        => SetStatusAsync(nodeId, credentialId, MagicCredentialStatus.Revoked, cancellationToken);
}
}
namespace MagicSettings.Server
{
public sealed class MagicCredentialRotationService
{
    private readonly IMagicCredentialRegistry _credentials;

    public MagicCredentialRotationService(IMagicCredentialRegistry credentials) => _credentials = credentials;

    public async ValueTask<MagicProofVerificationResult> ApplyAsync(
        MagicIdentityContinuityProof continuity,
        bool autoApproveNewCredential,
        CancellationToken cancellationToken = default)
    {
        if (!MagicNodeProofVerifier.VerifyContinuity(continuity))
        {
            return MagicProofVerificationResult.Invalid("The identity continuity proof is invalid.");
        }

        var previous = await _credentials.FindAsync(
            continuity.PreviousIdentity.NodeId,
            continuity.PreviousIdentity.CredentialId,
            cancellationToken);
        if (previous is null)
        {
            return MagicProofVerificationResult.Invalid("The previous credential is unknown.");
        }

        if (previous.Status is not (MagicCredentialStatus.Approved or MagicCredentialStatus.Retiring))
        {
            return MagicProofVerificationResult.Invalid("The previous credential is not authorized to rotate.");
        }

        if (!string.Equals(previous.PublicKey, continuity.PreviousIdentity.PublicKey, StringComparison.Ordinal))
        {
            return MagicProofVerificationResult.Invalid("The continuity proof does not match the registered previous key.");
        }

        await _credentials.UpsertAsync(previous with
        {
            Status = MagicCredentialStatus.Retiring,
            UpdatedUtc = DateTimeOffset.UtcNow
        }, cancellationToken);

        await _credentials.UpsertAsync(new(
            continuity.NewIdentity.NodeId,
            continuity.NewIdentity.CredentialId,
            continuity.NewIdentity.PublicKey,
            autoApproveNewCredential ? MagicCredentialStatus.Approved : MagicCredentialStatus.Pending,
            DateTimeOffset.UtcNow), cancellationToken);

        return MagicProofVerificationResult.Valid;
    }
}
}
namespace MagicSettings.Server
{
public sealed class InMemoryMagicCredentialRegistry : IMagicCredentialRegistry
{
    private readonly ConcurrentDictionary<(Guid NodeId, Guid CredentialId), MagicRegisteredCredential> _credentials = new();

    public ValueTask<MagicRegisteredCredential?> FindAsync(Guid nodeId, Guid credentialId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_credentials.TryGetValue((nodeId, credentialId), out var credential) ? credential : null);

    public ValueTask UpsertAsync(MagicRegisteredCredential credential, CancellationToken cancellationToken = default)
    {
        _credentials[(credential.NodeId, credential.CredentialId)] = credential;
        return ValueTask.CompletedTask;
    }
}

public sealed class InMemoryMagicReplayCache : IMagicReplayCache
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _nonces = new(StringComparer.Ordinal);

    public ValueTask<bool> TryUseAsync(Guid credentialId, string nonce, DateTimeOffset expiresUtc, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _nonces)
        {
            if (pair.Value <= now)
            {
                _nonces.TryRemove(pair.Key, out _);
            }
        }

        return ValueTask.FromResult(_nonces.TryAdd($"{credentialId:D}:{nonce}", expiresUtc));
    }
}

public sealed class InMemoryMagicNodeRemoteRecordStore : IMagicNodeRemoteRecordStore
{
    private readonly ConcurrentDictionary<(Guid NodeId, string ApplicationId), MagicNodeRemoteRecord> _records = new();

    public ValueTask<MagicNodeRemoteRecord?> GetAsync(Guid nodeId, string applicationId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_records.TryGetValue((nodeId, applicationId), out var record) ? record : null);

    public ValueTask SaveAsync(MagicNodeRemoteRecord record, CancellationToken cancellationToken = default)
    {
        _records[(record.NodeId, record.ApplicationId)] = record;
        return ValueTask.CompletedTask;
    }
}
}
namespace MagicSettings.Server
{
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
}
namespace MagicSettings.Server
{
public interface IMagicSecretResolver
{
    ValueTask<MagicSecretResponse> ResolveAsync(
        Guid nodeId,
        string name,
        CancellationToken cancellationToken = default);
}

public sealed class MagicSecretService
{
    private readonly MagicNodeProofVerifier _proofVerifier;
    private readonly IMagicSecretResolver _resolver;

    public MagicSecretService(MagicNodeProofVerifier proofVerifier, IMagicSecretResolver resolver)
    {
        _proofVerifier = proofVerifier;
        _resolver = resolver;
    }

    public async ValueTask<MagicSecretResponse> ResolveAsync(
        MagicSecretRequest request,
        string authorityAudience,
        Uri requestUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.NodeId != request.Proof.NodeId || request.CredentialId != request.Proof.CredentialId)
        {
            throw new UnauthorizedAccessException("The secret request identity does not match its proof.");
        }

        var verification = await _proofVerifier.VerifyAsync(
            new(
                request.Proof,
                authorityAudience,
                "POST",
                requestUri,
                MagicSecretProof.ComputeBodySha256(request.Name),
                DateTimeOffset.UtcNow),
            cancellationToken);
        if (!verification.IsValid)
        {
            throw new UnauthorizedAccessException(verification.Error);
        }

        return await _resolver.ResolveAsync(request.NodeId, request.Name, cancellationToken);
    }
}
}
