namespace MagicSettings;

public interface IMagicSettingsSourceProvider
{
    string Name { get; }
    int Priority { get; }
    ValueTask<IReadOnlyDictionary<string, string?>> LoadAsync(CancellationToken cancellationToken = default);
}
