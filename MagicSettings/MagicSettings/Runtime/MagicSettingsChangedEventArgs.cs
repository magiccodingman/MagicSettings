namespace MagicSettings;

public sealed record MagicSettingsChangedEventArgs<TSettings>(TSettings Previous, TSettings Current, long Revision) where TSettings : class;
