namespace MagicSettings.Server;

public interface IMagicNodeRemoteRecordStore
{
    ValueTask<MagicNodeRemoteRecord?> GetAsync(Guid nodeId, string applicationId, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(MagicNodeRemoteRecord record, CancellationToken cancellationToken = default);
}
