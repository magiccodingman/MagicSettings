namespace MagicSettings.Server;

public sealed class InMemoryMagicCredentialRegistry : IMagicCredentialRegistry
{
    private readonly ConcurrentDictionary<(Guid NodeId, Guid CredentialId), MagicRegisteredCredential> _credentials = new();

    public ValueTask<MagicRegisteredCredential?> FindAsync(Guid nodeId, Guid credentialId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_credentials.TryGetValue((nodeId, credentialId), out var credential) ? credential : null);

    public ValueTask UpsertAsync(MagicRegisteredCredential credential, CancellationToken cancellationToken = default)
    {
        _credentials[(credential.NodeId, credential.CredentialId)] = credential;
        return ValueTask.CompletedTask;
    }
}
