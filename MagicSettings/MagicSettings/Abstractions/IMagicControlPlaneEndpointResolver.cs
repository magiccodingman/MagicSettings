namespace MagicSettings;

public interface IMagicControlPlaneEndpointResolver
{
    MagicResolvedControlPlaneEndpoint Resolve<TSettings>(
        MagicSettingsOptions<TSettings> options,
        JsonObject persistentDocument,
        Uri? runtimeOverride = null) where TSettings : class, new();
}
