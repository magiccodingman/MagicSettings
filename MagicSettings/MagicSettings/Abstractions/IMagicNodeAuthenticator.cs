namespace MagicSettings;

public interface IMagicNodeAuthenticator
{
    ValueTask<MagicNodeIdentityDescriptor> GetCurrentIdentityAsync(CancellationToken cancellationToken = default);
    ValueTask<MagicAuthenticationProof> CreateProofAsync(MagicAuthenticationRequest request, CancellationToken cancellationToken = default);
}
