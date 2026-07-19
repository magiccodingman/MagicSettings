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

internal static class MagicJsonFlattener
{
    public static Dictionary<string, string?> Flatten(JsonObject root)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Visit(root, null, result);
        return result;
    }

    private static void Visit(JsonNode? node, string? path, IDictionary<string, string?> result)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    if (pair.Key == "$magicSettings")
                    {
                        continue;
                    }

                    Visit(pair.Value, path is null ? pair.Key : $"{path}:{pair.Key}", result);
                }
                break;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    Visit(array[index], $"{path}:{index}", result);
                }
                break;
            case JsonValue value when path is not null:
                result[path] = ToConfigurationString(value);
                break;
            case null when path is not null:
                result[path] = null;
                break;
        }
    }

    private static string? ToConfigurationString(JsonValue value)
    {
        if (value.TryGetValue<string>(out var text)) return text;
        if (value.TryGetValue<bool>(out var boolean)) return boolean ? "true" : "false";
        if (value.TryGetValue<int>(out var integer)) return integer.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<long>(out var longInteger)) return longInteger.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<double>(out var number)) return number.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<decimal>(out var decimalNumber)) return decimalNumber.ToString(CultureInfo.InvariantCulture);
        return value.ToJsonString();
    }
}
