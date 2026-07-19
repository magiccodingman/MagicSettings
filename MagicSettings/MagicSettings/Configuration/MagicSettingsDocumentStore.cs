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
