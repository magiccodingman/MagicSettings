namespace MagicSettings;

public sealed class MagicSecretLease<T> : IAsyncDisposable
{
    public MagicSecretLease(T value, DateTimeOffset? expiresUtc = null)
    {
        Value = value;
        ExpiresUtc = expiresUtc;
    }

    public T Value { get; }
    public DateTimeOffset? ExpiresUtc { get; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
