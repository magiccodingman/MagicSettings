namespace MagicSettings;

public static class MagicSettingsPathResolver
{
    public static string Resolve<TSettings>(MagicSettingsOptions<TSettings> options) where TSettings : class, new()
    {
        var configured = options.Path;
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable("MAGICSETTINGS_PATH");
        }

        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, options.FileName));
        }

        var fullPath = Path.GetFullPath(configured);
        if (Path.HasExtension(fullPath) && string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return Path.Combine(fullPath, options.FileName);
    }
}
