namespace MagicSettings;

public interface IMagicSettingsControlPlane
{
    MagicControlPlaneState State { get; }
    MagicResolvedControlPlaneEndpoint CurrentEndpoint { get; }
    ValueTask ConfigureAsync(Uri endpoint, MagicControlPlaneTrust trust, CancellationToken cancellationToken = default);
    ValueTask DisconnectAsync(bool clearRemoteOverrides = false, CancellationToken cancellationToken = default);
    ValueTask RefreshAsync(CancellationToken cancellationToken = default);
}
