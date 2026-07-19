namespace MagicSettings;

public sealed class MagicControlPlaneBootstrapOptions
{
    public string EnvironmentVariableName { get; set; } = "MAGICSETTINGS_CONTROL_PLANE_ENDPOINT";
    public string PersistentSettingPath { get; set; } = "MagicSettings:ControlPlane:Endpoint";
    public Uri? CodeFallbackEndpoint { get; set; }
    public MagicControlPlaneTrust? Trust { get; set; }
    public bool ConnectOnStartup { get; set; }
    public bool WatchPersistentEndpoint { get; set; } = true;
    public bool AllowInsecureHttp { get; set; }
}
