// Consolidated source file. See repository history and wiki for subsystem boundaries.
global using Xunit;
using MagicSettings.Server;
using MagicSettings.Share;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MagicSettings.Tests
{
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
}
namespace MagicSettings.Tests
{
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
}
namespace MagicSettings.Tests
{
public sealed class EndpointBootstrapTests
{
    [Fact]
    public void Resolver_UsesEnvironmentBeforePersistentAndFallback()
    {
        const string variable = "MAGICSETTINGS_TEST_CONTROL_PLANE";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "https://environment.example/");
            var options = new MagicSettingsOptions<TestSettings>();
            options.ControlPlane.Bootstrap.EnvironmentVariableName = variable;
            options.ControlPlane.Bootstrap.PersistentSettingPath = "MagicSettings:ControlPlane:Endpoint";
            options.ControlPlane.Bootstrap.CodeFallbackEndpoint = new Uri("https://fallback.example/");
            var document = JsonNode.Parse("""{"MagicSettings":{"ControlPlane":{"Endpoint":"https://file.example/"}}}""")!.AsObject();

            var resolved = new MagicControlPlaneEndpointResolver().Resolve(options, document);

            Assert.Equal(MagicControlPlaneEndpointSource.EnvironmentVariable, resolved.Source);
            Assert.Equal("https://environment.example/", resolved.Endpoint!.AbsoluteUri);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public void Resolver_UsesPersistentFileWithoutConsultingRemoteState()
    {
        var options = new MagicSettingsOptions<TestSettings>();
        options.ControlPlane.Bootstrap.EnvironmentVariableName = "MAGICSETTINGS_TEST_MISSING_ENDPOINT";
        options.ControlPlane.Bootstrap.PersistentSettingPath = "MagicSettings:ControlPlane:Endpoint";
        var local = JsonNode.Parse("""{"MagicSettings":{"ControlPlane":{"Endpoint":"https://local.example/"}}}""")!.AsObject();

        var resolved = new MagicControlPlaneEndpointResolver().Resolve(options, local);

        Assert.Equal(MagicControlPlaneEndpointSource.PersistentSettings, resolved.Source);
        Assert.Equal("https://local.example/", resolved.Endpoint!.AbsoluteUri);
    }

    [Fact]
    public void Resolver_RejectsNonLoopbackHttpUnlessExplicitlyAllowed()
    {
        var options = new MagicSettingsOptions<TestSettings>();
        options.ControlPlane.Bootstrap.CodeFallbackEndpoint = new Uri("http://control.example/");

        Assert.Throws<InvalidOperationException>(() =>
            new MagicControlPlaneEndpointResolver().Resolve(options, new JsonObject()));

        options.ControlPlane.Bootstrap.AllowInsecureHttp = true;
        var resolved = new MagicControlPlaneEndpointResolver().Resolve(options, new JsonObject());
        Assert.Equal("http://control.example/", resolved.Endpoint!.AbsoluteUri);
    }

    [Fact]
    public void RuntimeOverride_IsHighestPriority()
    {
        var options = new MagicSettingsOptions<TestSettings>();
        options.ControlPlane.Bootstrap.CodeFallbackEndpoint = new Uri("https://fallback.example/");
        var resolved = new MagicControlPlaneEndpointResolver().Resolve(options, new JsonObject(), new Uri("https://runtime.example/"));
        Assert.Equal(MagicControlPlaneEndpointSource.RuntimeOverride, resolved.Source);
        Assert.Equal("https://runtime.example/", resolved.Endpoint!.AbsoluteUri);
    }
}
}
namespace MagicSettings.Tests
{
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
}
namespace MagicSettings.Tests
{
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
}
