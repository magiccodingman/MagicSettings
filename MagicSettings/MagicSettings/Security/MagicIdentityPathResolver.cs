namespace MagicSettings;

public static class MagicIdentityPathResolver
{
    public static string Resolve(string settingsPath, string? configuredPath, string fileName)
    {
        var configured = string.IsNullOrWhiteSpace(configuredPath)
            ? Environment.GetEnvironmentVariable("MAGICSETTINGS_IDENTITY_PATH")
            : configuredPath;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(settingsPath) ?? AppContext.BaseDirectory, fileName));
        }

        var fullPath = Path.GetFullPath(configured);
        return Path.HasExtension(fullPath) ? fullPath : Path.Combine(fullPath, fileName);
    }
}
