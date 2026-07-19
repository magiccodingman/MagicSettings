# Migrations and collection reconciliation

Use explicit sequential migrations for breaking changes. A migration declares `FromVersion`, `ToVersion`, and mutates a `JsonObject` through `MagicMigrationContext`.

```csharp
public sealed class RenameDatabaseUser : IMagicSettingsMigration
{
    public int FromVersion => 1;
    public int ToVersion => 2;

    public void Apply(JsonObject document, MagicMigrationContext context)
    {
        context.Rename(document, "Database:Username", "Database:User");
    }
}
```

Missing steps, non-advancing versions, downgrade attempts, and destination collisions stop startup. The original document is not replaced until the complete candidate succeeds.

`context.Remove(...)` removes the path from the local document but creates a destructive review item for remote storage. The server helper retains that review item rather than deleting a stored secret automatically.
