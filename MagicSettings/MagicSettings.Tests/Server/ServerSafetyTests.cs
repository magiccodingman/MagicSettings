using MagicSettings.Server;
using MagicSettings.Share;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MagicSettings.Tests;

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
