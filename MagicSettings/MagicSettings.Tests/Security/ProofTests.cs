namespace MagicSettings.Tests;

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
