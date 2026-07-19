namespace MagicSettings;

public interface IMagicSecretTransport
{
    ValueTask<MagicSecretResponse> ResolveSecretAsync(
        Uri endpoint,
        MagicControlPlaneTrust trust,
        MagicSecretRequest request,
        CancellationToken cancellationToken = default);
}
