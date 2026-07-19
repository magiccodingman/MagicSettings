using MagicSettings.Server;
using MagicSettings.Share;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MagicSettings.Tests;

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
