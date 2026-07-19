namespace MagicSettings.Server;

public sealed class InMemoryMagicNodeRemoteRecordStore : IMagicNodeRemoteRecordStore
{
    private readonly ConcurrentDictionary<(Guid NodeId, string ApplicationId), MagicNodeRemoteRecord> _records = new();

    public ValueTask<MagicNodeRemoteRecord?> GetAsync(Guid nodeId, string applicationId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_records.TryGetValue((nodeId, applicationId), out var record) ? record : null);

    public ValueTask SaveAsync(MagicNodeRemoteRecord record, CancellationToken cancellationToken = default)
    {
        _records[(record.NodeId, record.ApplicationId)] = record;
        return ValueTask.CompletedTask;
    }
}
