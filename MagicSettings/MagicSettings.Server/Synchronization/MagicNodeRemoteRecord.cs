namespace MagicSettings.Server;

public sealed record MagicNodeRemoteRecord(
    Guid NodeId,
    string ApplicationId,
    int SchemaVersion,
    string SchemaFingerprint,
    long Revision,
    MagicRemoteSnapshot Snapshot,
    IReadOnlyList<MagicMigrationReviewItem> PendingReviewItems,
    DateTimeOffset LastContactUtc);
