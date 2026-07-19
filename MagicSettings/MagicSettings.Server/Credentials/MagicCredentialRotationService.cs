namespace MagicSettings.Server;

public sealed class MagicCredentialRotationService
{
    private readonly IMagicCredentialRegistry _credentials;

    public MagicCredentialRotationService(IMagicCredentialRegistry credentials) => _credentials = credentials;

    public async ValueTask<MagicProofVerificationResult> ApplyAsync(
        MagicIdentityContinuityProof continuity,
        bool autoApproveNewCredential,
        CancellationToken cancellationToken = default)
    {
        if (!MagicNodeProofVerifier.VerifyContinuity(continuity))
        {
            return MagicProofVerificationResult.Invalid("The identity continuity proof is invalid.");
        }

        var previous = await _credentials.FindAsync(
            continuity.PreviousIdentity.NodeId,
            continuity.PreviousIdentity.CredentialId,
            cancellationToken);
        if (previous is null)
        {
            return MagicProofVerificationResult.Invalid("The previous credential is unknown.");
        }

        if (previous.Status is not (MagicCredentialStatus.Approved or MagicCredentialStatus.Retiring))
        {
            return MagicProofVerificationResult.Invalid("The previous credential is not authorized to rotate.");
        }

        if (!string.Equals(previous.PublicKey, continuity.PreviousIdentity.PublicKey, StringComparison.Ordinal))
        {
            return MagicProofVerificationResult.Invalid("The continuity proof does not match the registered previous key.");
        }

        await _credentials.UpsertAsync(previous with
        {
            Status = MagicCredentialStatus.Retiring,
            UpdatedUtc = DateTimeOffset.UtcNow
        }, cancellationToken);

        await _credentials.UpsertAsync(new(
            continuity.NewIdentity.NodeId,
            continuity.NewIdentity.CredentialId,
            continuity.NewIdentity.PublicKey,
            autoApproveNewCredential ? MagicCredentialStatus.Approved : MagicCredentialStatus.Pending,
            DateTimeOffset.UtcNow), cancellationToken);

        return MagicProofVerificationResult.Valid;
    }
}
