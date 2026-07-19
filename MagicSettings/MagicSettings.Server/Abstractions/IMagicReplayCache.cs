using System.Security.Cryptography;
using MagicSettings.Share;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Text;

namespace MagicSettings.Server;

public interface IMagicReplayCache
{
    ValueTask<bool> TryUseAsync(Guid credentialId, string nonce, DateTimeOffset expiresUtc, CancellationToken cancellationToken = default);
}
