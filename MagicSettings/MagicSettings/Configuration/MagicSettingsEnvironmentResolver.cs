namespace MagicSettings;

public static class MagicSettingsEnvironmentResolver
{
    public static string Resolve(string? explicitEnvironment = null, string? hostEnvironment = null)
        => FirstNonEmpty(
            explicitEnvironment,
            Environment.GetEnvironmentVariable("MAGICSETTINGS_ENVIRONMENT"),
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            hostEnvironment,
            "Production")!;

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
}
