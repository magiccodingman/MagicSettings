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
