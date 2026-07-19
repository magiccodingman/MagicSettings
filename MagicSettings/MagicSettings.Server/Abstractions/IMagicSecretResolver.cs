using System.Security.Cryptography;
using MagicSettings.Share;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Text;

namespace MagicSettings.Server;

public interface IMagicSecretResolver
{
    ValueTask<MagicSecretResponse> ResolveAsync(
        Guid nodeId,
        string name,
        CancellationToken cancellationToken = default);
}
