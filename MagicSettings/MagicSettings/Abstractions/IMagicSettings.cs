namespace MagicSettings;

public interface IMagicSettings<TSettings> where TSettings : class
{
    TSettings Current { get; }
    long Revision { get; }
    event EventHandler<MagicSettingsChangedEventArgs<TSettings>>? Changed;
    MagicSettingExplanation Explain(string path);
}
