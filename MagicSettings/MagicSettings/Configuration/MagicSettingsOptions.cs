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

namespace MagicSettings;

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
