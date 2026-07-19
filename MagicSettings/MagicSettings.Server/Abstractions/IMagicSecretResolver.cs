namespace MagicSettings.Server;

public interface IMagicSecretResolver
{
    ValueTask<MagicSecretResponse> ResolveAsync(
        Guid nodeId,
        string name,
        CancellationToken cancellationToken = default);
}
