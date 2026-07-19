// Consolidated source file. See repository history and wiki for subsystem boundaries.
global using Xunit;
using MagicSettings.Server;
using MagicSettings.Share;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MagicSettings.Tests
{
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
}
namespace MagicSettings.Tests
{
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
}
namespace MagicSettings.Tests
{
public sealed class ProofTests
{
    [Fact]
    public async Task Proof_IsAudienceMethodTargetAndBodyBound_AndCannotReplay()
    {
        using var directory = new TemporaryDirectory();
        var manager = new MagicNodeIdentityManager(new FileMagicNodeIdentityStore(Path.Combine(directory.Path, "identity.json")));
        var identity = await manager.GetCurrentAsync();
        var registry = new InMemoryMagicCredentialRegistry();
        await registry.UpsertAsync(new(identity.NodeId, identity.CredentialId, identity.PublicKey, MagicCredentialStatus.Approved, DateTimeOffset.UtcNow));
        var verifier = new MagicNodeProofVerifier(registry, new InMemoryMagicReplayCache());
        var uri = new Uri("https://api.example/v1/items?x=1");
        var bodyHash = MagicHash.Sha256Hex("hello"u8);
        var proof = await manager.CreateProofAsync(new("Items.Api", "POST", uri, bodyHash));
        var request = new MagicProofVerificationRequest(proof, "Items.Api", "POST", uri, bodyHash, DateTimeOffset.UtcNow);

        Assert.True((await verifier.VerifyAsync(request)).IsValid);
        Assert.Equal("The proof nonce has already been used.", (await verifier.VerifyAsync(request)).Error);
    }

    [Fact]
    public async Task Proof_RejectsWrongAudienceBeforeNonceConsumption()
    {
        using var directory = new TemporaryDirectory();
        var manager = new MagicNodeIdentityManager(new FileMagicNodeIdentityStore(Path.Combine(directory.Path, "identity.json")));
        var identity = await manager.GetCurrentAsync();
        var registry = new InMemoryMagicCredentialRegistry();
        await registry.UpsertAsync(new(identity.NodeId, identity.CredentialId, identity.PublicKey, MagicCredentialStatus.Approved, DateTimeOffset.UtcNow));
        var verifier = new MagicNodeProofVerifier(registry, new InMemoryMagicReplayCache());
        var uri = new Uri("https://api.example/v1/items");
        var proof = await manager.CreateProofAsync(new("Items.Api", "GET", uri, MagicHash.EmptySha256));

        var wrong = await verifier.VerifyAsync(new(proof, "Admin.Api", "GET", uri, MagicHash.EmptySha256, DateTimeOffset.UtcNow));
        var correct = await verifier.VerifyAsync(new(proof, "Items.Api", "GET", uri, MagicHash.EmptySha256, DateTimeOffset.UtcNow));

        Assert.False(wrong.IsValid);
        Assert.True(correct.IsValid);
    }
}
}
namespace MagicSettings.Tests
{
public sealed class SecretTests
{
    [Fact]
    public async Task SecretRequestUsesFreshBoundNodeProof()
    {
        using var directory = new TemporaryDirectory();
        var manager = new MagicNodeIdentityManager(new FileMagicNodeIdentityStore(Path.Combine(directory.Path, "identity.json")));
        var identity = await manager.GetCurrentAsync();
        var registry = new InMemoryMagicCredentialRegistry();
        await registry.UpsertAsync(new(identity.NodeId, identity.CredentialId, identity.PublicKey, MagicCredentialStatus.Approved, DateTimeOffset.UtcNow));
        var verifier = new MagicNodeProofVerifier(registry, new InMemoryMagicReplayCache());
        var resolver = new FakeSecretResolver();
        var service = new MagicSecretService(verifier, resolver);
        var uri = new Uri("https://control.example/magicsettings/secrets/resolve");
        const string name = "Database:Password";
        var proof = await manager.CreateProofAsync(new("Control.Api", "POST", uri, MagicSecretProof.ComputeBodySha256(name)));

        var response = await service.ResolveAsync(new(identity.NodeId, identity.CredentialId, name, proof), "Control.Api", uri);

        Assert.True(response.Found);
        Assert.Equal("secret-value", response.Value);
    }

    private sealed class FakeSecretResolver : IMagicSecretResolver
    {
        public ValueTask<MagicSecretResponse> ResolveAsync(Guid nodeId, string name, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new MagicSecretResponse(true, "secret-value"));
    }
}
}
namespace MagicSettings.Tests
{
public sealed class ServerSafetyTests
{
    [Fact]
    public async Task SyncService_RetainsDestructiveMigrationAsManualReview()
    {
        using var directory = new TemporaryDirectory();
        var manager = new MagicNodeIdentityManager(new FileMagicNodeIdentityStore(Path.Combine(directory.Path, "identity.json")));
        var identity = await manager.GetCurrentAsync();
        var registry = new InMemoryMagicCredentialRegistry();
        await registry.UpsertAsync(new(identity.NodeId, identity.CredentialId, identity.PublicKey, MagicCredentialStatus.Approved, DateTimeOffset.UtcNow));
        var records = new InMemoryMagicNodeRemoteRecordStore();
        var verifier = new MagicNodeProofVerifier(registry, new InMemoryMagicReplayCache());
        var service = new MagicSettingsSyncService(registry, records, verifier);
        var uri = new Uri("https://control.example/magicsettings/sync");
        var report = new MagicSettingsMigrationReport(1, 2, [],
        [
            new("Secrets:Old", "remove", MagicMigrationReviewSeverity.Destructive, "Client no longer uses this secret.")
        ]);
        var manifest = new MagicSettingsSchemaManifest("TestApp", "1.0.0", 2, "fingerprint", []);
        var bodyHash = MagicSettingsSyncProof.ComputeBodySha256(identity, manifest, 0, report);
        var proof = await manager.CreateProofAsync(new("Control.Api", "POST", uri, bodyHash));

        var response = await service.SynchronizeAsync(new(identity, proof, manifest, 0, report), "Control.Api", uri);
        var stored = await records.GetAsync(identity.NodeId, "TestApp");

        Assert.Equal(MagicControlPlaneState.Active, response.State);
        Assert.Single(stored!.PendingReviewItems);
        Assert.Equal("Secrets:Old", stored.PendingReviewItems[0].Path);
    }
}
}
namespace MagicSettings.Tests
{
internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MagicSettings.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
}
namespace MagicSettings.Tests
{
public sealed class TestSettings
{
    public TestApplication Application { get; set; } = new();
    public TestDatabase Database { get; set; } = new();
    public TestControlPlane MagicSettings { get; set; } = new();
    public List<string> Origins { get; set; } = new();
}

public sealed class TestApplication
{
    public string Name { get; set; } = "Test";
    public bool FeatureEnabled { get; set; }
}

public sealed class TestDatabase
{
    public string Host { get; set; } = "localhost";
    public string Password { get; set; } = "change-me";
    public int Port { get; set; } = 5432;
}

public sealed class TestControlPlane
{
    public TestControlPlaneSection ControlPlane { get; set; } = new();
}

public sealed class TestControlPlaneSection
{
    public string? Endpoint { get; set; }
}
}
