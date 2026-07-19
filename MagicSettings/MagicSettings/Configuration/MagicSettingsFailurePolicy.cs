namespace MagicSettings;

public sealed class MagicSettingsFailurePolicy
{
    public bool StrictDevelopmentMode { get; set; } = true;
    public MagicFailureAction AmbiguousArrayInProduction { get; set; } = MagicFailureAction.WarnAndContinue;
    public MagicFailureAction RuntimeReloadFailure { get; set; } = MagicFailureAction.KeepLastKnownGood;
}
