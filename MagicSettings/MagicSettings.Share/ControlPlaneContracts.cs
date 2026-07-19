namespace MagicSettings.Share;

public enum MagicControlPlaneEndpointSource
{
    None,
    CodeFallback,
    PersistentSettings,
    EnvironmentVariable,
    RuntimeOverride
}

public sealed record MagicResolvedControlPlaneEndpoint(Uri? Endpoint, MagicControlPlaneEndpointSource Source)
{
    public static MagicResolvedControlPlaneEndpoint None { get; } = new(null, MagicControlPlaneEndpointSource.None);
}

public enum MagicControlPlaneState
{
    Disabled,
    Configured,
    Connecting,
    PendingApproval,
    Active,
    Disconnected,
    Faulted
}

public enum MagicControlPlaneTrustMode
{
    SystemTls,
    PinnedPublicKey
}

public sealed record MagicControlPlaneTrust(
    MagicControlPlaneTrustMode Mode,
    string AuthorityId,
    string? PinnedPublicKeyFingerprint = null)
{
    public static MagicControlPlaneTrust SystemTls(string authorityId) => new(MagicControlPlaneTrustMode.SystemTls, authorityId);
    public static MagicControlPlaneTrust Pinned(string authorityId, string fingerprint) => new(MagicControlPlaneTrustMode.PinnedPublicKey, authorityId, fingerprint);
}

public sealed record MagicSettingsSyncRequest(
    MagicNodeIdentityDescriptor Identity,
    MagicAuthenticationProof Proof,
    MagicSettingsSchemaManifest Manifest,
    long LastRemoteRevision,
    MagicSettingsMigrationReport? MigrationReport = null,
    MagicIdentityContinuityProof? IdentityContinuityProof = null);

public sealed record MagicSettingsSyncResponse(
    MagicControlPlaneState State,
    MagicRemoteSnapshot Snapshot,
    string? Message = null,
    TimeSpan? SuggestedPollInterval = null);

public sealed record MagicSecretRequest(Guid NodeId, Guid CredentialId, string Name, MagicAuthenticationProof Proof);
public sealed record MagicSecretResponse(bool Found, string? Value, DateTimeOffset? ExpiresUtc = null);
