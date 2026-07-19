// Consolidated source file. See repository history and wiki for subsystem boundaries.
using System.Text.Json.Nodes;
using MagicSettings.Share;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Collections;
using System.Globalization;
using System.ComponentModel.DataAnnotations;

namespace MagicSettings
{
public sealed class HttpMagicControlPlaneTransport : IMagicControlPlaneTransport, IMagicSecretTransport, IDisposable
{
    private readonly HttpClient _systemTlsClient;
    private readonly ConcurrentDictionary<string, HttpClient> _pinnedClients = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _json;

    public HttpMagicControlPlaneTransport(TimeSpan? timeout = null, JsonSerializerOptions? json = null)
    {
        _systemTlsClient = CreateClient(certificateValidator: null, timeout);
        _json = json ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public async ValueTask<MagicSettingsSyncResponse> SynchronizeAsync(
        Uri endpoint,
        MagicControlPlaneTrust trust,
        MagicSettingsSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(request);

        var client = SelectClient(trust);

        var syncUri = new Uri(endpoint, "magicsettings/sync");
        using var message = new HttpRequestMessage(HttpMethod.Post, syncUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, _json), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("MagicNode", MagicNodeProofCodec.Encode(request.Proof));
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<MagicSettingsSyncResponse>(stream, _json, cancellationToken)
            ?? throw new InvalidOperationException("The control plane returned an empty synchronization response.");
    }


    public async ValueTask<MagicSecretResponse> ResolveSecretAsync(
        Uri endpoint,
        MagicControlPlaneTrust trust,
        MagicSecretRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(request);

        var client = SelectClient(trust);
        var requestUri = new Uri(endpoint, "magicsettings/secrets/resolve");
        using var message = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, _json), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("MagicNode", MagicNodeProofCodec.Encode(request.Proof));
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<MagicSecretResponse>(stream, _json, cancellationToken)
            ?? throw new InvalidOperationException("The control plane returned an empty secret response.");
    }

    public void Dispose()
    {
        _systemTlsClient.Dispose();
        foreach (var client in _pinnedClients.Values)
        {
            client.Dispose();
        }
    }

    private HttpClient SelectClient(MagicControlPlaneTrust trust)
        => trust.Mode switch
        {
            MagicControlPlaneTrustMode.SystemTls => _systemTlsClient,
            MagicControlPlaneTrustMode.PinnedPublicKey => GetPinnedClient(trust),
            _ => throw new ArgumentOutOfRangeException(nameof(trust), trust.Mode, "Unsupported control-plane trust mode.")
        };

    private HttpClient GetPinnedClient(MagicControlPlaneTrust trust)
    {
        var expected = NormalizeFingerprint(trust.PinnedPublicKeyFingerprint
            ?? throw new InvalidOperationException("PinnedPublicKey trust requires a public-key fingerprint."));
        return _pinnedClients.GetOrAdd(expected, fingerprint => CreateClient(
            (_, certificate, _, _) => certificate is not null && FixedTimeEquals(PublicKeyFingerprint(certificate), fingerprint),
            _systemTlsClient.Timeout));
    }

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>? certificateValidator,
        TimeSpan? timeout)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        if (certificateValidator is not null)
        {
            handler.ServerCertificateCustomValidationCallback = certificateValidator;
        }

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(30)
        };
    }

    private static string PublicKeyFingerprint(X509Certificate2 certificate)
        => Convert.ToHexString(SHA256.HashData(certificate.GetPublicKey())).ToLowerInvariant();

    private static string NormalizeFingerprint(string value)
        => value.Replace(":", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();

    private static bool FixedTimeEquals(string actual, string expected)
    {
        var actualBytes = Encoding.ASCII.GetBytes(actual);
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        return actualBytes.Length == expectedBytes.Length
               && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
}
namespace MagicSettings
{
public sealed class FileMagicNodeIdentityStore : IMagicNodeIdentityStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public FileMagicNodeIdentityStore(string path) => _path = Path.GetFullPath(path);

    public async ValueTask<MagicStoredNodeIdentity?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<MagicStoredNodeIdentity>(stream, _json, cancellationToken);
    }

    public async ValueTask SaveAsync(MagicStoredNodeIdentity identity, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? AppContext.BaseDirectory);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, identity, _json, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        TryRestrictPermissions(temporary);
        File.Move(temporary, _path, overwrite: true);
        TryRestrictPermissions(_path);
    }

    public ValueTask DeleteAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        return ValueTask.CompletedTask;
    }

    private static void TryRestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}

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

public sealed class MagicNodeIdentityManager : IMagicNodeIdentityManager, IMagicNodeAuthenticator
{
    private readonly IMagicNodeIdentityStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MagicStoredNodeIdentity? _current;

    public MagicNodeIdentityManager(IMagicNodeIdentityStore store) => _store = store;

    public event EventHandler<MagicIdentityChange>? IdentityChanged;

    public async ValueTask<MagicNodeIdentityDescriptor> GetCurrentAsync(CancellationToken cancellationToken = default)
        => MagicIdentityCryptography.Describe(await GetStoredAsync(cancellationToken));

    public ValueTask<MagicNodeIdentityDescriptor> GetCurrentIdentityAsync(CancellationToken cancellationToken = default)
        => GetCurrentAsync(cancellationToken);

    public async ValueTask<MagicAuthenticationProof> CreateProofAsync(MagicAuthenticationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Method);
        ArgumentNullException.ThrowIfNull(request.Uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BodySha256);

        var identity = await GetStoredAsync(cancellationToken);
        var issued = DateTimeOffset.UtcNow;
        var validFor = request.ValidFor ?? TimeSpan.FromMinutes(1);
        if (validFor <= TimeSpan.Zero || validFor > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Authentication proofs must be valid for more than zero and no longer than five minutes.");
        }

        var unsigned = new MagicAuthenticationProof(
            MagicIdentityCryptography.ProofVersion,
            identity.NodeId,
            identity.CredentialId,
            request.Audience,
            request.Method.ToUpperInvariant(),
            MagicIdentityCryptography.NormalizeTarget(request.Uri),
            request.BodySha256.ToLowerInvariant(),
            issued,
            issued.Add(validFor),
            MagicNodeProofCodec.Base64UrlEncode(RandomNumberGenerator.GetBytes(24)),
            string.Empty);

        return unsigned with { Signature = MagicIdentityCryptography.Sign(identity, MagicIdentityCryptography.Canonicalize(unsigned)) };
    }

    public async ValueTask<MagicIdentityChange> RotateAsync(string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var previous = await LoadOrCreateUnderLockAsync(cancellationToken);
            var next = MagicIdentityCryptography.Create(previous.NodeId);
            var previousDescriptor = MagicIdentityCryptography.Describe(previous);
            var nextDescriptor = MagicIdentityCryptography.Describe(next);
            var issued = DateTimeOffset.UtcNow;
            var nonce = MagicNodeProofCodec.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
            var canonical = MagicIdentityCryptography.CanonicalizeContinuity(previousDescriptor, nextDescriptor, issued, nonce);
            var continuity = new MagicIdentityContinuityProof(previousDescriptor, nextDescriptor, issued, nonce, MagicIdentityCryptography.Sign(previous, canonical));
            await _store.SaveAsync(next, cancellationToken);
            _current = next;
            var change = new MagicIdentityChange(MagicIdentityChangeKind.Rotated, nextDescriptor, previousDescriptor, continuity, reason);
            IdentityChanged?.Invoke(this, change);
            return change;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<MagicIdentityChange> ResetAsync(MagicIdentityResetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmDestructiveReset)
        {
            throw new InvalidOperationException("Identity reset is destructive and requires ConfirmDestructiveReset=true.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var previous = await _store.LoadAsync(cancellationToken);
            var next = MagicIdentityCryptography.Create();
            await _store.SaveAsync(next, cancellationToken);
            _current = next;
            var change = new MagicIdentityChange(
                MagicIdentityChangeKind.Reset,
                MagicIdentityCryptography.Describe(next),
                previous is null ? null : MagicIdentityCryptography.Describe(previous),
                null,
                request.Reason);
            IdentityChanged?.Invoke(this, change);
            return change;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<MagicStoredNodeIdentity> GetStoredAsync(CancellationToken cancellationToken)
    {
        if (_current is not null)
        {
            return _current;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadOrCreateUnderLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<MagicStoredNodeIdentity> LoadOrCreateUnderLockAsync(CancellationToken cancellationToken)
    {
        if (_current is not null)
        {
            return _current;
        }

        _current = await _store.LoadAsync(cancellationToken);
        if (_current is null)
        {
            _current = MagicIdentityCryptography.Create();
            await _store.SaveAsync(_current, cancellationToken);
        }

        return _current;
    }
}

public static class MagicHash
{
    public static string Sha256Hex(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    public static string EmptySha256 { get; } = Sha256Hex(Array.Empty<byte>());
}
}
