namespace MagicSettings;

public interface IMagicSettingsMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    void Apply(JsonObject document, MagicMigrationContext context);
}
