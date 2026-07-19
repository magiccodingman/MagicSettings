using System.Security.Cryptography;
using MagicSettings.Share;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Text;

namespace MagicSettings.Server;

public interface IMagicNodeRemoteRecordStore
{
    ValueTask<MagicNodeRemoteRecord?> GetAsync(Guid nodeId, string applicationId, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(MagicNodeRemoteRecord record, CancellationToken cancellationToken = default);
}
