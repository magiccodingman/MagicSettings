namespace MagicSettings;

public interface IMagicNodeIdentityManager
{
    event EventHandler<MagicIdentityChange>? IdentityChanged;
    ValueTask<MagicNodeIdentityDescriptor> GetCurrentAsync(CancellationToken cancellationToken = default);
    ValueTask<MagicIdentityChange> RotateAsync(string reason, CancellationToken cancellationToken = default);
    ValueTask<MagicIdentityChange> ResetAsync(MagicIdentityResetRequest request, CancellationToken cancellationToken = default);
}
