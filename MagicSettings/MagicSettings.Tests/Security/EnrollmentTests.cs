namespace MagicSettings.Tests;

public sealed class EnrollmentTests
{
    [Fact]
    public async Task UnknownNodeMustProvePossessionBeforeBeingRegisteredAsPending()
    {
        using var directory = new TemporaryDirectory();
        var manager = new MagicNodeIdentityManager(new FileMagicNodeIdentityStore(Path.Combine(directory.Path, "identity.json")));
        var identity = await manager.GetCurrentAsync();
        var registry = new InMemoryMagicCredentialRegistry();
        var service = new MagicSettingsSyncService(
            registry,
            new InMemoryMagicNodeRemoteRecordStore(),
            new MagicNodeProofVerifier(registry, new InMemoryMagicReplayCache()));
        var uri = new Uri("https://control.example/magicsettings/sync");
        var manifest = new MagicSettingsSchemaManifest("TestApp", "1.0.0", 1, "fingerprint", []);
        var bodyHash = MagicSettingsSyncProof.ComputeBodySha256(identity, manifest, 0, null);
        var proof = await manager.CreateProofAsync(new("Control.Api", "POST", uri, bodyHash));

        var response = await service.SynchronizeAsync(new(identity, proof, manifest, 0), "Control.Api", uri);
        var registered = await registry.FindAsync(identity.NodeId, identity.CredentialId);

        Assert.Equal(MagicControlPlaneState.PendingApproval, response.State);
        Assert.Equal(MagicCredentialStatus.Pending, registered!.Status);
    }

    [Fact]
    public async Task UnknownNodeWithTamperedManifestIsRejected()
    {
        using var directory = new TemporaryDirectory();
        var manager = new MagicNodeIdentityManager(new FileMagicNodeIdentityStore(Path.Combine(directory.Path, "identity.json")));
        var identity = await manager.GetCurrentAsync();
        var registry = new InMemoryMagicCredentialRegistry();
        var service = new MagicSettingsSyncService(
            registry,
            new InMemoryMagicNodeRemoteRecordStore(),
            new MagicNodeProofVerifier(registry, new InMemoryMagicReplayCache()));
        var uri = new Uri("https://control.example/magicsettings/sync");
        var signedManifest = new MagicSettingsSchemaManifest("TestApp", "1.0.0", 1, "fingerprint-a", []);
        var sentManifest = signedManifest with { SchemaFingerprint = "fingerprint-b" };
        var bodyHash = MagicSettingsSyncProof.ComputeBodySha256(identity, signedManifest, 0, null);
        var proof = await manager.CreateProofAsync(new("Control.Api", "POST", uri, bodyHash));

        var response = await service.SynchronizeAsync(new(identity, proof, sentManifest, 0), "Control.Api", uri);

        Assert.Equal(MagicControlPlaneState.Faulted, response.State);
        Assert.Null(await registry.FindAsync(identity.NodeId, identity.CredentialId));
    }
}
