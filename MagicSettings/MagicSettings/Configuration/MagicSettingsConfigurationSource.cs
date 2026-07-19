namespace MagicSettings;

internal sealed class MagicSettingsConfigurationSource : IConfigurationSource
{
    private readonly MagicSettingsConfigurationProvider _provider;
    public MagicSettingsConfigurationSource(MagicSettingsConfigurationProvider provider) => _provider = provider;
    public IConfigurationProvider Build(IConfigurationBuilder builder) => _provider;
}
