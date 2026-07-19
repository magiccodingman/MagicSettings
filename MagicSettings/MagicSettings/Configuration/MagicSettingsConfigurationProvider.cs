namespace MagicSettings;

internal sealed class MagicSettingsConfigurationProvider : ConfigurationProvider
{
    public void Publish(IReadOnlyDictionary<string, string?> values)
    {
        Data = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
        OnReload();
    }
}
