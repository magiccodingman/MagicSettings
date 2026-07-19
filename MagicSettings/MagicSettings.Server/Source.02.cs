// Consolidated source file. See repository history and wiki for subsystem boundaries.
using System.Security.Cryptography;
using MagicSettings.Share;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Text;

namespace MagicSettings.Server
{
public sealed record MagicRegisteredCredential(
    Guid NodeId,
    Guid CredentialId,
    string PublicKey,
    MagicCredentialStatus Status,
    DateTimeOffset UpdatedUtc);

public interface IMagicCredentialRegistry
{
    ValueTask<MagicRegisteredCredential?> FindAsync(Guid nodeId, Guid credentialId, CancellationToken cancellationToken = default);
    ValueTask UpsertAsync(MagicRegisteredCredential credential, CancellationToken cancellationToken = default);
}

public interface IMagicReplayCache
{
    ValueTask<bool> TryUseAsync(Guid credentialId, string nonce, DateTimeOffset expiresUtc, CancellationToken cancellationToken = default);
}

public sealed record MagicNodeRemoteRecord(
    Guid NodeId,
    string ApplicationId,
    int SchemaVersion,
    string SchemaFingerprint,
    long Revision,
    MagicRemoteSnapshot Snapshot,
    IReadOnlyList<MagicMigrationReviewItem> PendingReviewItems,
    DateTimeOffset LastContactUtc);

public interface IMagicNodeRemoteRecordStore
{
    ValueTask<MagicNodeRemoteRecord?> GetAsync(Guid nodeId, string applicationId, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(MagicNodeRemoteRecord record, CancellationToken cancellationToken = default);
}
}
namespace MagicSettings.Server
{
public sealed class MagicSettingsSyncService
{
    private readonly IMagicCredentialRegistry _credentials;
    private readonly IMagicNodeRemoteRecordStore _records;
    private readonly MagicNodeProofVerifier _proofVerifier;
    private readonly MagicCredentialRotationService _rotationService;
    private readonly bool _autoApproveRotatedCredentials;

    public MagicSettingsSyncService(
        IMagicCredentialRegistry credentials,
        IMagicNodeRemoteRecordStore records,
        MagicNodeProofVerifier proofVerifier,
        bool autoApproveRotatedCredentials = false)
    {
        _credentials = credentials;
        _records = records;
        _proofVerifier = proofVerifier;
        _rotationService = new MagicCredentialRotationService(credentials);
        _autoApproveRotatedCredentials = autoApproveRotatedCredentials;
    }

    public async ValueTask<MagicSettingsSyncResponse> SynchronizeAsync(
        MagicSettingsSyncRequest request,
        string authorityAudience,
        Uri requestUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var signedPayloadHash = MagicSettingsSyncProof.ComputeBodySha256(
            request.Identity,
            request.Manifest,
            request.LastRemoteRevision,
            request.MigrationReport,
            request.IdentityContinuityProof);
        var verificationRequest = new MagicProofVerificationRequest(
            request.Proof,
            authorityAudience,
            "POST",
            requestUri,
            signedPayloadHash,
            DateTimeOffset.UtcNow);

        var registered = await _credentials.FindAsync(request.Identity.NodeId, request.Identity.CredentialId, cancellationToken);
        if (registered is null && request.IdentityContinuityProof is { } continuity)
        {
            if (continuity.NewIdentity.NodeId != request.Identity.NodeId
                || continuity.NewIdentity.CredentialId != request.Identity.CredentialId
                || !string.Equals(continuity.NewIdentity.PublicKey, request.Identity.PublicKey, StringComparison.Ordinal))
            {
                return new(MagicControlPlaneState.Faulted, MagicRemoteSnapshot.Empty, "The rotation proof does not describe the supplied current identity.");
            }

            var rotation = await _rotationService.ApplyAsync(continuity, _autoApproveRotatedCredentials, cancellationToken);
            if (!rotation.IsValid)
            {
                return new(MagicControlPlaneState.Faulted, MagicRemoteSnapshot.Empty, rotation.Error);
            }

            registered = await _credentials.FindAsync(request.Identity.NodeId, request.Identity.CredentialId, cancellationToken);
        }

        if (registered is null)
        {
            var enrollmentVerification = await _proofVerifier.VerifyEnrollmentAsync(request.Identity, verificationRequest, cancellationToken);
            if (!enrollmentVerification.IsValid)
            {
                return new(MagicControlPlaneState.Faulted, MagicRemoteSnapshot.Empty, enrollmentVerification.Error);
            }

            await _credentials.UpsertAsync(
                new(request.Identity.NodeId, request.Identity.CredentialId, request.Identity.PublicKey, MagicCredentialStatus.Pending, DateTimeOffset.UtcNow),
                cancellationToken);
            return new(MagicControlPlaneState.PendingApproval, MagicRemoteSnapshot.Empty, "Node credential is pending approval.");
        }

        if (registered.Status == MagicCredentialStatus.Pending)
        {
            return new(MagicControlPlaneState.PendingApproval, MagicRemoteSnapshot.Empty, "Node credential is pending approval.");
        }

        if (!string.Equals(registered.PublicKey, request.Identity.PublicKey, StringComparison.Ordinal))
        {
            return new(MagicControlPlaneState.Faulted, MagicRemoteSnapshot.Empty, "The supplied identity does not match the registered credential.");
        }

        var verification = await _proofVerifier.VerifyAsync(verificationRequest, cancellationToken);
        if (!verification.IsValid)
        {
            return new(MagicControlPlaneState.Faulted, MagicRemoteSnapshot.Empty, verification.Error);
        }

        var existing = await _records.GetAsync(request.Identity.NodeId, request.Manifest.ApplicationId, cancellationToken);
        var reviewItems = existing?.PendingReviewItems.ToList() ?? new List<MagicMigrationReviewItem>();
        if (request.MigrationReport is { } migrationReport)
        {
            foreach (var item in migrationReport.ReviewItems)
            {
                if (!reviewItems.Contains(item))
                {
                    reviewItems.Add(item);
                }
            }
        }

        var snapshot = existing?.Snapshot ?? MagicRemoteSnapshot.Empty;
        var updated = new MagicNodeRemoteRecord(
            request.Identity.NodeId,
            request.Manifest.ApplicationId,
            request.Manifest.SchemaVersion,
            request.Manifest.SchemaFingerprint,
            existing?.Revision ?? 0,
            snapshot,
            reviewItems,
            DateTimeOffset.UtcNow);
        await _records.SaveAsync(updated, cancellationToken);
        return new(MagicControlPlaneState.Active, snapshot);
    }
}
}
