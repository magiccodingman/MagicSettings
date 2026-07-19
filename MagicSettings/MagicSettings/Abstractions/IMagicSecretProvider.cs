namespace MagicSettings;

public interface IMagicSecretProvider
{
    ValueTask<MagicSecretLease<T>> GetAsync<T>(string name, CancellationToken cancellationToken = default);
}
