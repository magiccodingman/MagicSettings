namespace MagicSettings;

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
