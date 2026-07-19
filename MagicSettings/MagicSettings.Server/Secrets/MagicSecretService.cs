namespace MagicSettings.Server;

public sealed class MagicSecretService
{
    private readonly MagicNodeProofVerifier _proofVerifier;
    private readonly IMagicSecretResolver _resolver;

    public MagicSecretService(MagicNodeProofVerifier proofVerifier, IMagicSecretResolver resolver)
    {
        _proofVerifier = proofVerifier;
        _resolver = resolver;
    }

    public async ValueTask<MagicSecretResponse> ResolveAsync(
        MagicSecretRequest request,
        string authorityAudience,
        Uri requestUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.NodeId != request.Proof.NodeId || request.CredentialId != request.Proof.CredentialId)
        {
            throw new UnauthorizedAccessException("The secret request identity does not match its proof.");
        }

        var verification = await _proofVerifier.VerifyAsync(
            new(
                request.Proof,
                authorityAudience,
                "POST",
                requestUri,
                MagicSecretProof.ComputeBodySha256(request.Name),
                DateTimeOffset.UtcNow),
            cancellationToken);
        if (!verification.IsValid)
        {
            throw new UnauthorizedAccessException(verification.Error);
        }

        return await _resolver.ResolveAsync(request.NodeId, request.Name, cancellationToken);
    }
}
