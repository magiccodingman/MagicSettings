namespace MagicSettings;

public interface IMagicNodeIdentityStore
{
    ValueTask<MagicStoredNodeIdentity?> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsync(MagicStoredNodeIdentity identity, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(CancellationToken cancellationToken = default);
}
