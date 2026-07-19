namespace MagicSettings.Tests;

public sealed class CredentialRotationTests
{
    [Fact]
    public async Task ServerCanApproveRotatedCredentialWithoutReceivingPrivateKey()
    {
        using var directory = new TemporaryDirectory();
        var manager = new MagicNodeIdentityManager(new FileMagicNodeIdentityStore(Path.Combine(directory.Path, "identity.json")));
        var initial = await manager.GetCurrentAsync();
        var registry = new InMemoryMagicCredentialRegistry();
        await registry.UpsertAsync(new(initial.NodeId, initial.CredentialId, initial.PublicKey, MagicCredentialStatus.Approved, DateTimeOffset.UtcNow));
        var rotation = await manager.RotateAsync("scheduled");

        var result = await new MagicCredentialRotationService(registry).ApplyAsync(rotation.ContinuityProof!, autoApproveNewCredential: true);
        var oldCredential = await registry.FindAsync(initial.NodeId, initial.CredentialId);
        var newCredential = await registry.FindAsync(rotation.Current.NodeId, rotation.Current.CredentialId);

        Assert.True(result.IsValid);
        Assert.Equal(MagicCredentialStatus.Retiring, oldCredential!.Status);
        Assert.Equal(MagicCredentialStatus.Approved, newCredential!.Status);
    }
    [Fact]
    public async Task SyncCanCarryContinuityProofAndAutoApproveRotatedCredential()
    {
        using var directory = new TemporaryDirectory();
        var manager = new MagicNodeIdentityManager(new FileMagicNodeIdentityStore(Path.Combine(directory.Path, "identity.json")));
        var initial = await manager.GetCurrentAsync();
        var registry = new InMemoryMagicCredentialRegistry();
        await registry.UpsertAsync(new(initial.NodeId, initial.CredentialId, initial.PublicKey, MagicCredentialStatus.Approved, DateTimeOffset.UtcNow));
        var change = await manager.RotateAsync("scheduled");
        var records = new InMemoryMagicNodeRemoteRecordStore();
        var verifier = new MagicNodeProofVerifier(registry, new InMemoryMagicReplayCache());
        var service = new MagicSettingsSyncService(registry, records, verifier, autoApproveRotatedCredentials: true);
        var uri = new Uri("https://control.example/magicsettings/sync");
        var manifest = new MagicSettingsSchemaManifest("TestApp", "1.0.0", 1, "fingerprint", []);
        var bodyHash = MagicSettingsSyncProof.ComputeBodySha256(change.Current, manifest, 0, null, change.ContinuityProof);
        var proof = await manager.CreateProofAsync(new("Control.Api", "POST", uri, bodyHash));

        var response = await service.SynchronizeAsync(
            new(change.Current, proof, manifest, 0, null, change.ContinuityProof),
            "Control.Api",
            uri);

        Assert.Equal(MagicControlPlaneState.Active, response.State);
        Assert.Equal(
            MagicCredentialStatus.Approved,
            (await registry.FindAsync(change.Current.NodeId, change.Current.CredentialId))!.Status);
    }

}
