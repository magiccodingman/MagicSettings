namespace MagicSettings;

public sealed record MagicSettingsInitializationResult(bool ShouldExit, int ExitCode, string SettingsPath, string Environment);
