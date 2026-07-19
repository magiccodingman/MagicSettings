namespace MagicSettings;

public sealed class MagicControlPlaneOptions
{
    public MagicControlPlaneBootstrapOptions Bootstrap { get; } = new();
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(90);
    public TimeSpan PollJitter { get; set; } = TimeSpan.FromSeconds(30);
    public bool KeepLastKnownGoodDuringOutage { get; set; } = true;
}
