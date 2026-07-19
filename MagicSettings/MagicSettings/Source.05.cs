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
public static class MagicSettingsInitializationExtensions
{
    public static async ValueTask<MagicSettingsInitializationResult> AddMagicSettingsAsync<TSettings>(
        this IHostApplicationBuilder builder,
        string[]? args = null,
        Action<MagicSettingsOptions<TSettings>>? configure = null,
        CancellationToken cancellationToken = default)
        where TSettings : class, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new MagicSettingsOptions<TSettings>();
        configure?.Invoke(options);
        options.Environment = MagicSettingsEnvironmentResolver.Resolve(options.Environment, builder.Environment.EnvironmentName);
        if (options.EnvironmentProfiles.TryGetValue(options.Environment, out var environmentProfile))
        {
            environmentProfile(options.Template);
        }

        var command = MagicSettingsCommandLine.Parse(args ?? Array.Empty<string>());
        var path = MagicSettingsPathResolver.Resolve(options);
        var documentResult = await MagicSettingsDocumentStore.LoadAndReconcileAsync(
            path,
            options,
            command.ForceGenerate,
            cancellationToken);

        if (command.PrintPath)
        {
            Console.Out.WriteLine(path);
        }

        var identityPath = MagicIdentityPathResolver.Resolve(path, options.IdentityPath, options.IdentityFileName);
        var identityStore = options.IdentityStore ?? new FileMagicNodeIdentityStore(identityPath);
        var identityManager = new MagicNodeIdentityManager(identityStore);
        await identityManager.GetCurrentAsync(cancellationToken);

        var configurationProvider = new MagicSettingsConfigurationProvider();
        var runtime = new MagicSettingsRuntime<TSettings>(options, configurationProvider, path);
        await runtime.InitializeAsync(documentResult.Document, cancellationToken);

        var endpointResolver = options.ControlPlaneEndpointResolver ?? new MagicControlPlaneEndpointResolver();
        var transport = options.ControlPlaneTransport ?? new HttpMagicControlPlaneTransport();
        var controlPlane = new MagicSettingsControlPlane<TSettings>(
            options,
            runtime,
            identityManager,
            identityManager,
            transport,
            endpointResolver,
            documentResult.Document,
            documentResult.MigrationReport);

        builder.Configuration.Add(new MagicSettingsConfigurationSource(configurationProvider));
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IMagicSettings<TSettings>>(runtime);
        builder.Services.AddSingleton(runtime);
        builder.Services.AddSingleton<IMagicNodeIdentityManager>(identityManager);
        builder.Services.AddSingleton<IMagicNodeAuthenticator>(identityManager);
        builder.Services.AddSingleton<IMagicSettingsControlPlane>(controlPlane);
        builder.Services.AddSingleton(controlPlane);
        builder.Services.AddSingleton<IMagicControlPlaneEndpointResolver>(endpointResolver);
        builder.Services.AddSingleton<IMagicControlPlaneTransport>(transport);
        if (transport is IMagicSecretTransport secretTransport)
        {
            builder.Services.AddSingleton<IMagicSecretTransport>(secretTransport);
            builder.Services.AddSingleton<IMagicSecretProvider>(
                new MagicControlPlaneSecretProvider<TSettings>(controlPlane, identityManager, secretTransport, options));
        }
        builder.Services.AddSingleton(new MagicSettingsRuntimeRegistration(path));
        builder.Services.AddHostedService(serviceProvider =>
            new MagicSettingsHostedService<TSettings>(
                options,
                runtime,
                controlPlane,
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MagicSettingsHostedService<TSettings>>>(),
                path));
        builder.Services.AddOptions<TSettings>().Bind(builder.Configuration);

        var shouldExit = command.GenerateOnly || command.ValidateOnly || command.PrintPath;
        return new(shouldExit, 0, path, options.Environment);
    }
}

public sealed record MagicSettingsRuntimeRegistration(string SettingsPath);

internal sealed record MagicSettingsCommandLine(bool GenerateOnly, bool ForceGenerate, bool ValidateOnly, bool PrintPath)
{
    public static MagicSettingsCommandLine Parse(IEnumerable<string> args)
    {
        var set = args.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var force = set.Contains("--magic-settings-force-generate");
        return new(
            set.Contains("--magic-settings-generate") || force,
            force,
            set.Contains("--magic-settings-validate"),
            set.Contains("--magic-settings-print-path"));
    }
}
}
namespace MagicSettings
{
internal static class MagicJsonPath
{
    public static bool TryGet(JsonObject root, string path, out JsonNode? value)
    {
        value = root;
        foreach (var segment in Split(path))
        {
            if (value is not JsonObject obj || !TryGetProperty(obj, segment, out _, out value))
            {
                value = null;
                return false;
            }
        }

        return true;
    }

    public static void Set(JsonObject root, string path, JsonNode? value)
    {
        var segments = Split(path);
        if (segments.Length == 0)
        {
            throw new ArgumentException("A setting path is required.", nameof(path));
        }

        var current = root;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            var segment = segments[index];
            if (!TryGetProperty(current, segment, out var actualName, out var existing) || existing is not JsonObject child)
            {
                child = new JsonObject();
                current[actualName ?? segment] = child;
            }

            current = child;
        }

        if (TryGetProperty(current, segments[^1], out var finalName, out _))
        {
            current[finalName!] = value?.DeepClone();
        }
        else
        {
            current[segments[^1]] = value?.DeepClone();
        }
    }

    public static bool TryTake(JsonObject root, string path, out JsonNode? value)
    {
        value = null;
        var segments = Split(path);
        if (segments.Length == 0)
        {
            return false;
        }

        var current = root;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (!TryGetProperty(current, segments[index], out _, out var existing) || existing is not JsonObject child)
            {
                return false;
            }

            current = child;
        }

        if (!TryGetProperty(current, segments[^1], out var actualName, out value))
        {
            return false;
        }

        current.Remove(actualName!);
        return true;
    }

    public static bool TryGetProperty(JsonObject obj, string name, out string? actualName, out JsonNode? value)
    {
        if (obj.TryGetPropertyValue(name, out value))
        {
            actualName = name;
            return true;
        }

        foreach (var pair in obj)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                actualName = pair.Key;
                value = pair.Value;
                return true;
            }
        }

        actualName = null;
        value = null;
        return false;
    }

    public static string[] Split(string path)
        => path.Split(new[] { ':', '.' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal static class MagicJsonFlattener
{
    public static Dictionary<string, string?> Flatten(JsonObject root)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Visit(root, null, result);
        return result;
    }

    private static void Visit(JsonNode? node, string? path, IDictionary<string, string?> result)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    if (pair.Key == "$magicSettings")
                    {
                        continue;
                    }

                    Visit(pair.Value, path is null ? pair.Key : $"{path}:{pair.Key}", result);
                }
                break;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    Visit(array[index], $"{path}:{index}", result);
                }
                break;
            case JsonValue value when path is not null:
                result[path] = ToConfigurationString(value);
                break;
            case null when path is not null:
                result[path] = null;
                break;
        }
    }

    private static string? ToConfigurationString(JsonValue value)
    {
        if (value.TryGetValue<string>(out var text)) return text;
        if (value.TryGetValue<bool>(out var boolean)) return boolean ? "true" : "false";
        if (value.TryGetValue<int>(out var integer)) return integer.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<long>(out var longInteger)) return longInteger.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<double>(out var number)) return number.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<decimal>(out var decimalNumber)) return decimalNumber.ToString(CultureInfo.InvariantCulture);
        return value.ToJsonString();
    }
}

internal static class MagicEnvironmentOverrides
{
    public static Dictionary<string, string?> Read(string prefix)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string name || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = name[prefix.Length..].Replace("__", ":", StringComparison.Ordinal);
            result[path] = entry.Value?.ToString();
        }

        return result;
    }
}
}
namespace MagicSettings
{
public sealed class MagicMigrationContext
{
    private readonly List<string> _safeOperations = new();
    private readonly List<MagicMigrationReviewItem> _reviewItems = new();

    public IReadOnlyList<string> SafeOperations => _safeOperations;
    public IReadOnlyList<MagicMigrationReviewItem> ReviewItems => _reviewItems;

    public void RecordSafe(string operation) => _safeOperations.Add(operation);

    public void RequireReview(string path, string operation, string reason, string? proposedPath = null, MagicMigrationReviewSeverity severity = MagicMigrationReviewSeverity.Warning)
        => _reviewItems.Add(new(path, operation, severity, reason, proposedPath));

    public void Rename(JsonObject document, string from, string to, bool remoteSafeProjection = true)
    {
        if (!MagicJsonPath.TryTake(document, from, out var value))
        {
            return;
        }

        if (MagicJsonPath.TryGet(document, to, out _))
        {
            throw new InvalidOperationException($"Cannot rename '{from}' to '{to}' because the destination already exists.");
        }

        MagicJsonPath.Set(document, to, value);
        RecordSafe($"rename:{from}->{to}");
        if (!remoteSafeProjection)
        {
            RequireReview(from, "rename", "The remote record should retain the original value until reviewed.", to);
        }
    }

    public void Transform(
        JsonObject document,
        string path,
        Func<JsonNode?, JsonNode?> transform,
        string description,
        bool remoteSafeProjection = false)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (!MagicJsonPath.TryGet(document, path, out var current))
        {
            return;
        }

        MagicJsonPath.Set(document, path, transform(current?.DeepClone()));
        RecordSafe($"transform:{path}:{description}");
        if (!remoteSafeProjection)
        {
            RequireReview(path, "transform", description);
        }
    }

    public void SetIfMissing(JsonObject document, string path, JsonNode? value)
    {
        if (MagicJsonPath.TryGet(document, path, out _))
        {
            return;
        }

        MagicJsonPath.Set(document, path, value);
        RecordSafe($"set-if-missing:{path}");
    }

    public void Remove(JsonObject document, string path, string reason)
    {
        if (MagicJsonPath.TryTake(document, path, out _))
        {
            RecordSafe($"local-remove:{path}");
            RequireReview(path, "remove", reason, severity: MagicMigrationReviewSeverity.Destructive);
        }
    }
}

internal static class MagicMigrationRunner
{
    public static MagicSettingsMigrationReport? Run(JsonObject document, int currentVersion, int targetVersion, IEnumerable<IMagicSettingsMigration> migrations)
    {
        if (currentVersion > targetVersion)
        {
            throw new InvalidOperationException($"Downgrading settings from schema {currentVersion} to {targetVersion} is not supported.");
        }

        if (currentVersion == targetVersion)
        {
            return null;
        }

        var ordered = migrations.OrderBy(static migration => migration.FromVersion).ToList();
        var context = new MagicMigrationContext();
        var version = currentVersion;
        while (version < targetVersion)
        {
            var migration = ordered.SingleOrDefault(item => item.FromVersion == version)
                ?? throw new InvalidOperationException($"No migration exists from schema version {version}.");
            if (migration.ToVersion <= version)
            {
                throw new InvalidOperationException($"Migration {migration.GetType().Name} does not advance the schema version.");
            }

            migration.Apply(document, context);
            version = migration.ToVersion;
        }

        if (version != targetVersion)
        {
            throw new InvalidOperationException($"Migration chain ended at schema version {version}, expected {targetVersion}.");
        }

        return new(currentVersion, targetVersion, context.SafeOperations.ToArray(), context.ReviewItems.ToArray());
    }
}
}
