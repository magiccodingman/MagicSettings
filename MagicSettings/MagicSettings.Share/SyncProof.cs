using System.Security.Cryptography;
using System.Text.Json;

namespace MagicSettings.Share;

/// <summary>
/// Produces the deterministic payload hash signed for a control-plane synchronization request.
/// The proof itself is intentionally excluded so the request can be signed without a circular dependency.
/// </summary>
public static class MagicSettingsSyncProof
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string ComputeBodySha256(
        MagicNodeIdentityDescriptor identity,
        MagicSettingsSchemaManifest manifest,
        long lastRemoteRevision,
        MagicSettingsMigrationReport? migrationReport,
        MagicIdentityContinuityProof? identityContinuityProof = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(manifest);

        var payload = new MagicSettingsSyncSigningPayload(
            identity.NodeId,
            identity.CredentialId,
            identity.PublicKey,
            manifest.ApplicationId,
            manifest.ApplicationVersion,
            manifest.SchemaVersion,
            manifest.SchemaFingerprint,
            lastRemoteRevision,
            migrationReport,
            identityContinuityProof);

        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload, Json))).ToLowerInvariant();
    }

    private sealed record MagicSettingsSyncSigningPayload(
        Guid NodeId,
        Guid CredentialId,
        string PublicKey,
        string ApplicationId,
        string ApplicationVersion,
        int SchemaVersion,
        string SchemaFingerprint,
        long LastRemoteRevision,
        MagicSettingsMigrationReport? MigrationReport,
        MagicIdentityContinuityProof? IdentityContinuityProof);
}
