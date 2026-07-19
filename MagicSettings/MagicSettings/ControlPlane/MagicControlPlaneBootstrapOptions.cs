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
