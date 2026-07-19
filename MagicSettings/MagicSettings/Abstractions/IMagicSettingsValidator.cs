namespace MagicSettings;

public interface IMagicSettingsValidator<in TSettings> where TSettings : class
{
    ValueTask<IReadOnlyList<string>> ValidateAsync(TSettings settings, CancellationToken cancellationToken = default);
}
