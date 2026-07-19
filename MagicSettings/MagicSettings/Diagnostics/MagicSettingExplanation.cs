namespace MagicSettings;

public sealed record MagicSettingExplanation(
    string Path,
    string? EffectiveValue,
    string EffectiveSource,
    IReadOnlyList<MagicSettingSourceValue> Sources,
    bool IsSensitive);
