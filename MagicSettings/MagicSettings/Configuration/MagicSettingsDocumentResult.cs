namespace MagicSettings;

internal sealed record MagicSettingsDocumentResult(JsonObject Document, MagicSettingsMigrationReport? MigrationReport, bool Changed);
