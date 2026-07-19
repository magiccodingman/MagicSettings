namespace MagicSettings;

public interface IMagicControlPlaneTransport
{
    ValueTask<MagicSettingsSyncResponse> SynchronizeAsync(
        Uri endpoint,
        MagicControlPlaneTrust trust,
        MagicSettingsSyncRequest request,
        CancellationToken cancellationToken = default);
}
