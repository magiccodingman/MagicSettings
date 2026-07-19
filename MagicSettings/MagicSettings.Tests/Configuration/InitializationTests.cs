namespace MagicSettings.Tests;

public sealed class InitializationTests
{
    [Fact]
    public async Task Initialization_GeneratesFileAndPreservesExistingValuesWhileAddingDefaults()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var builder = new HostApplicationBuilder();
        await builder.AddMagicSettingsAsync<TestSettings>(configure: options =>
        {
            options.Path = path;
            options.Template.Database.Host = "template-host";
            options.Template.Database.Port = 5432;
        });

        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        document["Database"]!["Host"] = "operator-host";
        document["Database"]!.AsObject().Remove("Port");
        await File.WriteAllTextAsync(path, document.ToJsonString(new() { WriteIndented = true }));

        var secondBuilder = new HostApplicationBuilder();
        await secondBuilder.AddMagicSettingsAsync<TestSettings>(configure: options =>
        {
            options.Path = path;
            options.Template.Database.Host = "new-template-host";
            options.Template.Database.Port = 7777;
        });

        var reconciled = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal("operator-host", reconciled["Database"]!["Host"]!.GetValue<string>());
        Assert.Equal(7777, reconciled["Database"]!["Port"]!.GetValue<int>());
    }

    [Fact]
    public async Task RemoteSnapshot_OverridesEnvironmentAndFileWithoutPersisting()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var transport = new FakeTransport("Database:Host", MagicRemoteValue.From("remote-host"));
        var previous = Environment.GetEnvironmentVariable("MagicSettings__Database__Host");
        Environment.SetEnvironmentVariable("MagicSettings__Database__Host", "environment-host");
        try
        {
            var builder = new HostApplicationBuilder();
            await builder.AddMagicSettingsAsync<TestSettings>(configure: options =>
            {
                options.Path = path;
                options.Template.Database.Host = "file-host";
                options.ControlPlaneTransport = transport;
            });
            await using var provider = builder.Services.BuildServiceProvider();
            var controlPlane = provider.GetRequiredService<IMagicSettingsControlPlane>();
            await controlPlane.ConfigureAsync(new Uri("https://control.example/"), MagicControlPlaneTrust.SystemTls("Control.Api"));
            var settings = provider.GetRequiredService<IMagicSettings<TestSettings>>();

            Assert.Equal("remote-host", settings.Current.Database.Host);
            var file = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            Assert.Equal("file-host", file["Database"]!["Host"]!.GetValue<string>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("MagicSettings__Database__Host", previous);
        }
    }

    [Fact]
    public async Task RemoteSnapshot_CannotOverrideControlPlaneBootstrapEndpoint()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var transport = new FakeTransport(
            "MagicSettings:ControlPlane:Endpoint",
            MagicRemoteValue.From("https://attacker.example/"));
        var builder = new HostApplicationBuilder();
        await builder.AddMagicSettingsAsync<TestSettings>(configure: options =>
        {
            options.Path = path;
            options.Template.MagicSettings.ControlPlane.Endpoint = "https://trusted-local.example/";
            options.ControlPlaneTransport = transport;
        });
        await using var provider = builder.Services.BuildServiceProvider();
        await provider.GetRequiredService<IMagicSettingsControlPlane>().ConfigureAsync(
            new Uri("https://control.example/"),
            MagicControlPlaneTrust.SystemTls("Control.Api"));

        var settings = provider.GetRequiredService<IMagicSettings<TestSettings>>();
        Assert.Equal("https://trusted-local.example/", settings.Current.MagicSettings.ControlPlane.Endpoint);
    }

    [Fact]
    public async Task DevelopmentRequiresExplicitArrayPolicy()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var first = new HostApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Development" });
        await first.AddMagicSettingsAsync<TestSettings>(configure: options =>
        {
            options.Path = path;
            options.Environment = "Development";
            options.Template.Origins = ["https://one.example"];
        });

        var second = new HostApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Development" });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await second.AddMagicSettingsAsync<TestSettings>(configure: options =>
            {
                options.Path = path;
                options.Environment = "Development";
                options.Template.Origins = ["https://one.example", "https://two.example"];
            }));
    }

    private sealed class FakeTransport : IMagicControlPlaneTransport
    {
        private readonly string _path;
        private readonly MagicRemoteValue _value;
        public FakeTransport(string path, MagicRemoteValue value)
        {
            _path = path;
            _value = value;
        }

        public ValueTask<MagicSettingsSyncResponse> SynchronizeAsync(Uri endpoint, MagicControlPlaneTrust trust, MagicSettingsSyncRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new MagicSettingsSyncResponse(
                MagicControlPlaneState.Active,
                new MagicRemoteSnapshot(1, DateTimeOffset.UtcNow, new Dictionary<string, MagicRemoteValue>
                {
                    [_path] = _value
                })));
    }
}
