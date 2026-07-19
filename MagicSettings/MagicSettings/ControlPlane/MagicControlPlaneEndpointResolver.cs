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

public sealed class MagicControlPlaneEndpointResolver : IMagicControlPlaneEndpointResolver
{
    public MagicResolvedControlPlaneEndpoint Resolve<TSettings>(
        MagicSettingsOptions<TSettings> options,
        JsonObject persistentDocument,
        Uri? runtimeOverride = null) where TSettings : class, new()
    {
        if (runtimeOverride is not null)
        {
            return new(Validate(runtimeOverride, "runtime override", options.ControlPlane.Bootstrap.AllowInsecureHttp), MagicControlPlaneEndpointSource.RuntimeOverride);
        }

        var environmentValue = Environment.GetEnvironmentVariable(options.ControlPlane.Bootstrap.EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return new(ParseAbsolute(environmentValue, options.ControlPlane.Bootstrap.EnvironmentVariableName, options.ControlPlane.Bootstrap.AllowInsecureHttp), MagicControlPlaneEndpointSource.EnvironmentVariable);
        }

        if (MagicJsonPath.TryGet(persistentDocument, options.ControlPlane.Bootstrap.PersistentSettingPath, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var persistentValue)
            && !string.IsNullOrWhiteSpace(persistentValue))
        {
            return new(ParseAbsolute(persistentValue, options.ControlPlane.Bootstrap.PersistentSettingPath, options.ControlPlane.Bootstrap.AllowInsecureHttp), MagicControlPlaneEndpointSource.PersistentSettings);
        }

        return options.ControlPlane.Bootstrap.CodeFallbackEndpoint is { } fallback
            ? new(Validate(fallback, "code fallback", options.ControlPlane.Bootstrap.AllowInsecureHttp), MagicControlPlaneEndpointSource.CodeFallback)
            : MagicResolvedControlPlaneEndpoint.None;
    }

    private static Uri ParseAbsolute(string value, string source, bool allowInsecureHttp)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException($"Control-plane endpoint from '{source}' must be an absolute URI.");
        }

        return Validate(endpoint, source, allowInsecureHttp);
    }

    internal static Uri Validate(Uri endpoint, string source, bool allowInsecureHttp)
    {
        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Control-plane endpoint from '{source}' must use HTTP or HTTPS.");
        }

        if (string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !allowInsecureHttp
            && !endpoint.IsLoopback)
        {
            throw new InvalidOperationException($"Control-plane endpoint from '{source}' must use HTTPS unless insecure HTTP is explicitly enabled or the endpoint is loopback.");
        }

        return endpoint;
    }
}
