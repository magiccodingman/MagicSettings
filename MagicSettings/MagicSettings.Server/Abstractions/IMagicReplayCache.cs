namespace MagicSettings.Server;

public interface IMagicReplayCache
{
    ValueTask<bool> TryUseAsync(Guid credentialId, string nonce, DateTimeOffset expiresUtc, CancellationToken cancellationToken = default);
}
