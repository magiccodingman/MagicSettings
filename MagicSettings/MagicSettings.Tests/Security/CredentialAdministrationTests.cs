using MagicSettings.Server;
using MagicSettings.Share;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MagicSettings.Tests;

public sealed class CredentialAdministrationTests
{
    [Fact]
    public async Task RevokedCredentialCanNoLongerAuthenticate()
    {
        using var directory = new TemporaryDirectory();
        var manager = new MagicNodeIdentityManager(new FileMagicNodeIdentityStore(Path.Combine(directory.Path, "identity.json")));
        var identity = await manager.GetCurrentAsync();
        var registry = new InMemoryMagicCredentialRegistry();
        await registry.UpsertAsync(new(identity.NodeId, identity.CredentialId, identity.PublicKey, MagicCredentialStatus.Approved, DateTimeOffset.UtcNow));
        var administration = new MagicCredentialAdministrationService(registry);
        Assert.True(await administration.RevokeAsync(identity.NodeId, identity.CredentialId));

        var uri = new Uri("https://api.example/resource");
        var proof = await manager.CreateProofAsync(new("Resource.Api", "GET", uri, MagicHash.EmptySha256));
        var result = await new MagicNodeProofVerifier(registry, new InMemoryMagicReplayCache()).VerifyAsync(
            new(proof, "Resource.Api", "GET", uri, MagicHash.EmptySha256, DateTimeOffset.UtcNow));

        Assert.False(result.IsValid);
        Assert.Equal("The credential is Revoked.", result.Error);
    }
}
