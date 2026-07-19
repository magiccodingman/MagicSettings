namespace MagicSettings.Tests;

public sealed class IdentityTests
{
    [Fact]
    public async Task Identity_IsStableAcrossManagerInstances()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "identity.json");
        var first = new MagicNodeIdentityManager(new FileMagicNodeIdentityStore(path));
        var firstIdentity = await first.GetCurrentAsync();
        var second = new MagicNodeIdentityManager(new FileMagicNodeIdentityStore(path));
        var secondIdentity = await second.GetCurrentAsync();

        Assert.Equal(firstIdentity.NodeId, secondIdentity.NodeId);
        Assert.Equal(firstIdentity.CredentialId, secondIdentity.CredentialId);
        Assert.Equal(firstIdentity.Fingerprint, secondIdentity.Fingerprint);
    }

    [Fact]
    public async Task Rotation_KeepsNodeAndProducesVerifiableContinuityProof()
    {
        using var directory = new TemporaryDirectory();
        var manager = new MagicNodeIdentityManager(new FileMagicNodeIdentityStore(Path.Combine(directory.Path, "identity.json")));
        var before = await manager.GetCurrentAsync();

        var change = await manager.RotateAsync("scheduled rotation");

        Assert.Equal(MagicIdentityChangeKind.Rotated, change.Kind);
        Assert.Equal(before.NodeId, change.Current.NodeId);
        Assert.NotEqual(before.CredentialId, change.Current.CredentialId);
        Assert.NotNull(change.ContinuityProof);
        Assert.True(MagicNodeProofVerifier.VerifyContinuity(change.ContinuityProof!));
    }

    [Fact]
    public async Task Reset_ChangesNodeAndRequiresExplicitConfirmation()
    {
        using var directory = new TemporaryDirectory();
        var manager = new MagicNodeIdentityManager(new FileMagicNodeIdentityStore(Path.Combine(directory.Path, "identity.json")));
        var before = await manager.GetCurrentAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ResetAsync(new("test", false)));

        var change = await manager.ResetAsync(new("compromised", true));
        Assert.Equal(MagicIdentityChangeKind.Reset, change.Kind);
        Assert.NotEqual(before.NodeId, change.Current.NodeId);
        Assert.Null(change.ContinuityProof);
    }
}
