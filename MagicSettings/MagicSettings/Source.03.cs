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
internal sealed record MagicSettingsDocumentResult(JsonObject Document, MagicSettingsMigrationReport? MigrationReport, bool Changed);

internal static class MagicSettingsDocumentStore
{
    private const string MetadataProperty = "$magicSettings";

    public static async ValueTask<MagicSettingsDocumentResult> LoadAndReconcileAsync<TSettings>(
        string path,
        MagicSettingsOptions<TSettings> options,
        bool forceRegenerate,
        CancellationToken cancellationToken) where TSettings : class, new()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
        var template = JsonSerializer.SerializeToNode(options.Template, options.Json) as JsonObject
            ?? throw new InvalidOperationException("The settings template must serialize as a JSON object.");

        JsonObject document;
        var existed = File.Exists(path);
        if (!existed || forceRegenerate)
        {
            document = (JsonObject)template.DeepClone();
        }
        else
        {
            await using var stream = File.OpenRead(path);
            document = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject
                ?? throw new InvalidOperationException($"Settings file '{path}' must contain a JSON object.");
        }

        var currentVersion = GetSchemaVersion(document);
        MagicSettingsMigrationReport? report = null;
        if (existed && !forceRegenerate && currentVersion < options.SchemaVersion)
        {
            report = MagicMigrationRunner.Run(document, currentVersion, options.SchemaVersion, options.Migrations);
        }

        var strict = options.Failures.StrictDevelopmentMode && IsDevelopment(options.Environment);
        var changed = !existed || forceRegenerate;
        changed |= ReconcileObject(document, template, string.Empty, options, strict);
        changed |= WriteMetadata(document, options);

        if (changed)
        {
            await WriteAtomicAsync(path, document, options.Json, cancellationToken);
        }

        return new(document, report, changed);
    }

    public static async ValueTask<JsonObject> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject
            ?? throw new InvalidOperationException($"Settings file '{path}' must contain a JSON object.");
    }

    private static bool ReconcileObject<TSettings>(JsonObject existing, JsonObject template, string path, MagicSettingsOptions<TSettings> options, bool strict)
        where TSettings : class, new()
    {
        var changed = false;
        foreach (var pair in template)
        {
            var childPath = string.IsNullOrEmpty(path) ? pair.Key : $"{path}:{pair.Key}";
            if (!MagicJsonPath.TryGetProperty(existing, pair.Key, out var actualPropertyName, out var existingValue))
            {
                existing[pair.Key] = pair.Value?.DeepClone();
                changed = true;
                continue;
            }

            if (existingValue is JsonObject existingObject && pair.Value is JsonObject templateObject)
            {
                changed |= ReconcileObject(existingObject, templateObject, childPath, options, strict);
                continue;
            }

            if (existingValue is JsonArray existingArray && pair.Value is JsonArray templateArray && !JsonNode.DeepEquals(existingArray, templateArray))
            {
                if (!options.ArrayPolicies.TryGetValue(childPath, out var policy))
                {
                    if (strict)
                    {
                        throw new InvalidOperationException($"Array reconciliation for '{childPath}' is ambiguous. Configure an explicit MagicArrayMergePolicy.");
                    }

                    policy = MagicArrayMergePolicy.PreserveExisting;
                }

                changed |= ApplyArrayPolicy(existing, actualPropertyName ?? pair.Key, existingArray, templateArray, policy);
            }
        }

        return changed;
    }

    private static bool ApplyArrayPolicy(JsonObject parent, string propertyName, JsonArray existing, JsonArray template, MagicArrayMergePolicy policy)
    {
        switch (policy)
        {
            case MagicArrayMergePolicy.PreserveExisting:
                return false;
            case MagicArrayMergePolicy.ReplaceWithTemplate:
                parent[propertyName] = template.DeepClone();
                return true;
            case MagicArrayMergePolicy.AppendMissing:
            case MagicArrayMergePolicy.Union:
                var changed = false;
                foreach (var item in template)
                {
                    if (existing.Any(candidate => JsonNode.DeepEquals(candidate, item)))
                    {
                        continue;
                    }

                    existing.Add(item?.DeepClone());
                    changed = true;
                }

                return changed;
            default:
                throw new ArgumentOutOfRangeException(nameof(policy), policy, null);
        }
    }

    private static int GetSchemaVersion(JsonObject document)
        => document[MetadataProperty]?["schemaVersion"]?.GetValue<int>() ?? 1;

    private static bool WriteMetadata<TSettings>(JsonObject document, MagicSettingsOptions<TSettings> options) where TSettings : class, new()
    {
        var metadata = document[MetadataProperty] as JsonObject ?? new JsonObject();
        var before = metadata.ToJsonString();
        metadata["schemaVersion"] = options.SchemaVersion;
        metadata["applicationId"] = options.ApplicationId;
        metadata["generatedBy"] = "MagicSettings";
        document[MetadataProperty] = metadata;
        return !string.Equals(before, metadata.ToJsonString(), StringComparison.Ordinal);
    }

    private static async ValueTask WriteAtomicAsync(string path, JsonObject document, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporary, document.ToJsonString(options), cancellationToken);
        if (File.Exists(path))
        {
            var backup = $"{path}.bak";
            File.Copy(path, backup, overwrite: true);
            File.Move(temporary, path, overwrite: true);
        }
        else
        {
            File.Move(temporary, path);
        }
    }

    private static bool IsDevelopment(string environment)
        => environment.Equals("Development", StringComparison.OrdinalIgnoreCase)
           || environment.Equals("Local", StringComparison.OrdinalIgnoreCase)
           || environment.Equals("Test", StringComparison.OrdinalIgnoreCase);
}
}
namespace MagicSettings
{
internal sealed class MagicSettingsHostedService<TSettings> : BackgroundService where TSettings : class, new()
{
    private readonly MagicSettingsOptions<TSettings> _options;
    private readonly MagicSettingsRuntime<TSettings> _runtime;
    private readonly MagicSettingsControlPlane<TSettings> _controlPlane;
    private readonly ILogger<MagicSettingsHostedService<TSettings>> _logger;
    private readonly string _settingsPath;
    private DateTime _lastWriteUtc;
    private DateTimeOffset _nextRemotePoll;

    public MagicSettingsHostedService(
        MagicSettingsOptions<TSettings> options,
        MagicSettingsRuntime<TSettings> runtime,
        MagicSettingsControlPlane<TSettings> controlPlane,
        ILogger<MagicSettingsHostedService<TSettings>> logger,
        string settingsPath)
    {
        _options = options;
        _runtime = runtime;
        _controlPlane = controlPlane;
        _logger = logger;
        _settingsPath = settingsPath;
        _lastWriteUtc = File.Exists(settingsPath) ? File.GetLastWriteTimeUtc(settingsPath) : DateTime.MinValue;
        _nextRemotePoll = DateTimeOffset.UtcNow.Add(options.ControlPlane.PollInterval);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _controlPlane.BootstrapAsync(stoppingToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "MagicSettings could not establish the bootstrap control-plane connection. Local configuration remains active.");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            _runtime.RefreshRemoteExpirations(DateTimeOffset.UtcNow);
            if (_options.ReloadOnChange)
            {
                await CheckPersistentFileAsync(stoppingToken);
            }

            if (DateTimeOffset.UtcNow >= _nextRemotePoll)
            {
                await PollRemoteAsync(stoppingToken);
                _nextRemotePoll = DateTimeOffset.UtcNow.Add(JitteredPollInterval());
            }
        }
    }

    private async ValueTask CheckPersistentFileAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return;
        }

        var currentWrite = File.GetLastWriteTimeUtc(_settingsPath);
        if (currentWrite <= _lastWriteUtc)
        {
            return;
        }

        await Task.Delay(_options.ReloadDebounce, cancellationToken);
        try
        {
            await _runtime.ReloadPersistentAsync(cancellationToken);
            var document = await MagicSettingsDocumentStore.ReadAsync(_settingsPath, cancellationToken);
            await _controlPlane.ReevaluatePersistentEndpointAsync(document, cancellationToken);
            _lastWriteUtc = currentWrite;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "MagicSettings rejected an invalid runtime reload and retained the last known good snapshot.");
        }
    }

    private async ValueTask PollRemoteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _controlPlane.RefreshAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "MagicSettings could not refresh remote overrides. The last known good remote snapshot remains active.");
        }
    }

    private TimeSpan JitteredPollInterval()
    {
        var jitter = _options.ControlPlane.PollJitter;
        if (jitter <= TimeSpan.Zero)
        {
            return _options.ControlPlane.PollInterval;
        }

        var milliseconds = Random.Shared.NextDouble() * jitter.TotalMilliseconds * 2 - jitter.TotalMilliseconds;
        return _options.ControlPlane.PollInterval + TimeSpan.FromMilliseconds(milliseconds);
    }
}
}
namespace MagicSettings
{
public sealed class MagicNodeAuthenticationHandler : DelegatingHandler
{
    private readonly IMagicNodeAuthenticator _authenticator;
    private readonly string _audience;

    public MagicNodeAuthenticationHandler(IMagicNodeAuthenticator authenticator, string audience)
    {
        _authenticator = authenticator;
        _audience = audience;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.RequestUri);
        var body = request.Content is null
            ? Array.Empty<byte>()
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        if (request.Content is not null)
        {
            var replacement = new ByteArrayContent(body);
            foreach (var header in request.Content.Headers)
            {
                replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            request.Content = replacement;
        }

        var proof = await _authenticator.CreateProofAsync(
            new(
                _audience,
                request.Method.Method,
                request.RequestUri,
                MagicHash.Sha256Hex(body)),
            cancellationToken);

        request.Headers.Authorization = new AuthenticationHeaderValue("MagicNode", MagicNodeProofCodec.Encode(proof));
        request.Headers.TryAddWithoutValidation("X-Magic-Node-Id", proof.NodeId.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-Magic-Credential-Id", proof.CredentialId.ToString("D"));
        return await base.SendAsync(request, cancellationToken);
    }
}

public static class MagicHttpClientBuilderExtensions
{
    public static IHttpClientBuilder AddMagicNodeAuthentication(this IHttpClientBuilder builder, string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        return builder.AddHttpMessageHandler(serviceProvider =>
            new MagicNodeAuthenticationHandler(serviceProvider.GetRequiredService<IMagicNodeAuthenticator>(), audience));
    }
}
}
