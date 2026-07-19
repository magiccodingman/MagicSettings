namespace MagicSettings.Server;

public interface IMagicCredentialRegistry
{
    ValueTask<MagicRegisteredCredential?> FindAsync(Guid nodeId, Guid credentialId, CancellationToken cancellationToken = default);
    ValueTask UpsertAsync(MagicRegisteredCredential credential, CancellationToken cancellationToken = default);
}
