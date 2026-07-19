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
