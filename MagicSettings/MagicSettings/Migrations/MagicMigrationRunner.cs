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
