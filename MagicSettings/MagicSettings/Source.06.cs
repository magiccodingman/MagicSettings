// Consolidated source file. See repository history and wiki for subsystem boundaries.
using System.Text.Json.Nodes;
using MagicSettings.Share;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Collections;
using System.Globalization;
using System.ComponentModel.DataAnnotations;

namespace MagicSettings
{
public enum MagicFailureAction
{
    StopStartup,
    WarnAndContinue,
    KeepLastKnownGood
}

public enum MagicArrayMergePolicy
{
    PreserveExisting,
    ReplaceWithTemplate,
    AppendMissing,
    Union
}

public sealed class MagicSettingsFailurePolicy
{
    public bool StrictDevelopmentMode { get; set; } = true;
    public MagicFailureAction AmbiguousArrayInProduction { get; set; } = MagicFailureAction.WarnAndContinue;
    public MagicFailureAction RuntimeReloadFailure { get; set; } = MagicFailureAction.KeepLastKnownGood;
}

public sealed class MagicControlPlaneBootstrapOptions
{
    public string EnvironmentVariableName { get; set; } = "MAGICSETTINGS_CONTROL_PLANE_ENDPOINT";
    public string PersistentSettingPath { get; set; } = "MagicSettings:ControlPlane:Endpoint";
    public Uri? CodeFallbackEndpoint { get; set; }
    public MagicControlPlaneTrust? Trust { get; set; }
    public bool ConnectOnStartup { get; set; }
    public bool WatchPersistentEndpoint { get; set; } = true;
    public bool AllowInsecureHttp { get; set; }
}

public sealed class MagicControlPlaneOptions
{
    public MagicControlPlaneBootstrapOptions Bootstrap { get; } = new();
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(90);
    public TimeSpan PollJitter { get; set; } = TimeSpan.FromSeconds(30);
    public bool KeepLastKnownGoodDuringOutage { get; set; } = true;
}

public sealed class MagicSettingsOptions<TSettings> where TSettings : class, new()
{
    public string ApplicationId { get; set; } = typeof(TSettings).Assembly.GetName().Name ?? typeof(TSettings).Name;
    public string ApplicationVersion { get; set; } = typeof(TSettings).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    public int SchemaVersion { get; set; } = 1;
    public TSettings Template { get; set; } = new();
    public string? Path { get; set; }
    public string FileName { get; set; } = "appsettings.json";
    public string EnvironmentOverridePrefix { get; set; } = "MagicSettings__";
    public string Environment { get; set; } = string.Empty;
    public bool ReloadOnChange { get; set; } = true;
    public TimeSpan ReloadDebounce { get; set; } = TimeSpan.FromMilliseconds(350);
    public bool PreserveUnknownProperties { get; set; } = true;
    public string IdentityFileName { get; set; } = ".magicsettings.identity.json";
    public string? IdentityPath { get; set; }
    public JsonSerializerOptions Json { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public MagicSettingsFailurePolicy Failures { get; } = new();
    public MagicControlPlaneOptions ControlPlane { get; } = new();
    public IDictionary<string, Action<TSettings>> EnvironmentProfiles { get; } = new Dictionary<string, Action<TSettings>>(StringComparer.OrdinalIgnoreCase);
    public IList<IMagicSettingsMigration> Migrations { get; } = new List<IMagicSettingsMigration>();
    public IList<IMagicSettingsValidator<TSettings>> Validators { get; } = new List<IMagicSettingsValidator<TSettings>>();
    public IList<IMagicSettingsSourceProvider> Providers { get; } = new List<IMagicSettingsSourceProvider>();
    public IDictionary<string, MagicArrayMergePolicy> ArrayPolicies { get; } = new Dictionary<string, MagicArrayMergePolicy>(StringComparer.OrdinalIgnoreCase);
    public ISet<string> SensitivePaths { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IMagicControlPlaneTransport? ControlPlaneTransport { get; set; }
    public IMagicControlPlaneEndpointResolver? ControlPlaneEndpointResolver { get; set; }
    public IMagicNodeIdentityStore? IdentityStore { get; set; }

    public void ConfigureEnvironment(string environment, Action<TSettings> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        ArgumentNullException.ThrowIfNull(configure);
        EnvironmentProfiles[environment] = configure;
    }
}

public sealed record MagicSettingsInitializationResult(bool ShouldExit, int ExitCode, string SettingsPath, string Environment);
}
namespace MagicSettings
{
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
