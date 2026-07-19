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

namespace MagicSettings;

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
