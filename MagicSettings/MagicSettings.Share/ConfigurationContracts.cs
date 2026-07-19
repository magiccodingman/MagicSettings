using System.Collections.ObjectModel;
using System.Text.Json;

namespace MagicSettings.Share;

public enum MagicValueState
{
    Value,
    Null
}

public enum MagicRemoteValueDurability
{
    Sticky,
    Refreshable,
    Expiring
}

public sealed record MagicRemoteValue(
    MagicValueState State,
    JsonElement? Value,
    MagicRemoteValueDurability Durability = MagicRemoteValueDurability.Sticky,
    DateTimeOffset? ExpiresUtc = null)
{
    public static MagicRemoteValue From<T>(T value, MagicRemoteValueDurability durability = MagicRemoteValueDurability.Sticky, DateTimeOffset? expiresUtc = null)
        => new(MagicValueState.Value, JsonSerializer.SerializeToElement(value), durability, expiresUtc);

    public static MagicRemoteValue ExplicitNull(MagicRemoteValueDurability durability = MagicRemoteValueDurability.Sticky, DateTimeOffset? expiresUtc = null)
        => new(MagicValueState.Null, null, durability, expiresUtc);
}

public sealed record MagicRemoteSnapshot(
    long Revision,
    DateTimeOffset IssuedUtc,
    IReadOnlyDictionary<string, MagicRemoteValue> Values)
{
    public static MagicRemoteSnapshot Empty { get; } = new(0, DateTimeOffset.MinValue, new ReadOnlyDictionary<string, MagicRemoteValue>(new Dictionary<string, MagicRemoteValue>()));
}

public sealed record MagicSettingManifestEntry(
    string Path,
    string Type,
    bool Nullable,
    bool Sensitive,
    bool RemoteOverrideAllowed,
    string? Description = null);

public sealed record MagicSettingsSchemaManifest(
    string ApplicationId,
    string ApplicationVersion,
    int SchemaVersion,
    string SchemaFingerprint,
    IReadOnlyList<MagicSettingManifestEntry> Settings);

public enum MagicMigrationReviewSeverity
{
    Information,
    Warning,
    Destructive
}

public sealed record MagicMigrationReviewItem(
    string Path,
    string Operation,
    MagicMigrationReviewSeverity Severity,
    string Reason,
    string? ProposedPath = null);

public sealed record MagicSettingsMigrationReport(
    int FromVersion,
    int ToVersion,
    IReadOnlyList<string> SafeOperations,
    IReadOnlyList<MagicMigrationReviewItem> ReviewItems);
