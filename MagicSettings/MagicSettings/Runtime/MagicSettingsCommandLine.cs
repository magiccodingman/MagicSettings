namespace MagicSettings;

internal sealed record MagicSettingsCommandLine(bool GenerateOnly, bool ForceGenerate, bool ValidateOnly, bool PrintPath)
{
    public static MagicSettingsCommandLine Parse(IEnumerable<string> args)
    {
        var set = args.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var force = set.Contains("--magic-settings-force-generate");
        return new(
            set.Contains("--magic-settings-generate") || force,
            force,
            set.Contains("--magic-settings-validate"),
            set.Contains("--magic-settings-print-path"));
    }
}
