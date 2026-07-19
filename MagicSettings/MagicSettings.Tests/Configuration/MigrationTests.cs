namespace MagicSettings.Tests;

public sealed class MigrationTests
{
    [Fact]
    public async Task SequentialMigrationRenamesAndTransformsWithoutDeletingRemoteHistory()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(path, """
        {
          "Database": {
            "Server": "old-host",
            "Port": "7443"
          },
          "$magicSettings": {
            "schemaVersion": 1
          }
        }
        """);

        var builder = new HostApplicationBuilder();
        await builder.AddMagicSettingsAsync<TestSettings>(configure: options =>
        {
            options.Path = path;
            options.SchemaVersion = 2;
            options.Migrations.Add(new VersionOneToTwo());
        });

        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal("old-host", document["Database"]!["Host"]!.GetValue<string>());
        Assert.Equal(7443, document["Database"]!["Port"]!.GetValue<int>());
        Assert.Equal(2, document["$magicSettings"]!["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public async Task MissingMigrationFailsStartupInEveryEnvironment()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(path, """{"$magicSettings":{"schemaVersion":1}}""");
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production" });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await builder.AddMagicSettingsAsync<TestSettings>(configure: options =>
            {
                options.Path = path;
                options.SchemaVersion = 2;
            }));
    }

    private sealed class VersionOneToTwo : IMagicSettingsMigration
    {
        public int FromVersion => 1;
        public int ToVersion => 2;

        public void Apply(JsonObject document, MagicMigrationContext context)
        {
            context.Rename(document, "Database:Server", "Database:Host");
            context.Transform(
                document,
                "Database:Port",
                node => JsonValue.Create(int.Parse(node!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)),
                "Convert port from string to integer.",
                remoteSafeProjection: false);
        }
    }
}
