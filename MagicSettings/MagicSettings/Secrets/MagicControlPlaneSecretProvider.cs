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

internal sealed class MagicControlPlaneSecretProvider<TSettings> : IMagicSecretProvider where TSettings : class, new()
{
    private readonly MagicSettingsControlPlane<TSettings> _controlPlane;
    private readonly IMagicNodeAuthenticator _authenticator;
    private readonly IMagicSecretTransport _transport;
    private readonly JsonSerializerOptions _json;

    public MagicControlPlaneSecretProvider(
        MagicSettingsControlPlane<TSettings> controlPlane,
        IMagicNodeAuthenticator authenticator,
        IMagicSecretTransport transport,
        MagicSettingsOptions<TSettings> options)
    {
        _controlPlane = controlPlane;
        _authenticator = authenticator;
        _transport = transport;
        _json = options.Json;
    }

    public async ValueTask<MagicSecretLease<T>> GetAsync<T>(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_controlPlane.TryGetActiveConnection(out var endpoint, out var trust))
        {
            throw new InvalidOperationException("The MagicSettings control plane is not active.");
        }

        var requestUri = new Uri(endpoint, "magicsettings/secrets/resolve");
        var proof = await _authenticator.CreateProofAsync(
            new(trust.AuthorityId, "POST", requestUri, MagicSecretProof.ComputeBodySha256(name)),
            cancellationToken);
        var identity = await _authenticator.GetCurrentIdentityAsync(cancellationToken);
        var response = await _transport.ResolveSecretAsync(
            endpoint,
            trust,
            new(identity.NodeId, identity.CredentialId, name, proof),
            cancellationToken);

        if (!response.Found)
        {
            throw new KeyNotFoundException($"MagicSettings secret '{name}' was not found.");
        }

        object? value;
        if (typeof(T) == typeof(string))
        {
            value = response.Value;
        }
        else
        {
            value = response.Value is null ? default(T) : JsonSerializer.Deserialize<T>(response.Value, _json);
        }

        if (value is null)
        {
            throw new InvalidOperationException($"MagicSettings secret '{name}' could not be converted to {typeof(T).FullName}.");
        }

        return new MagicSecretLease<T>((T)value, response.ExpiresUtc);
    }
}
