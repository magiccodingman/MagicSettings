using System.Security.Cryptography;
using MagicSettings.Share;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Text;

namespace MagicSettings.Server;

public sealed class InMemoryMagicReplayCache : IMagicReplayCache
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _nonces = new(StringComparer.Ordinal);

    public ValueTask<bool> TryUseAsync(Guid credentialId, string nonce, DateTimeOffset expiresUtc, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _nonces)
        {
            if (pair.Value <= now)
            {
                _nonces.TryRemove(pair.Key, out _);
            }
        }

        return ValueTask.FromResult(_nonces.TryAdd($"{credentialId:D}:{nonce}", expiresUtc));
    }
}
