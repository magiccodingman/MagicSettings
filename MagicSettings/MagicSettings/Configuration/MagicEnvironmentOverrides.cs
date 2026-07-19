namespace MagicSettings;

internal static class MagicEnvironmentOverrides
{
    public static Dictionary<string, string?> Read(string prefix)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string name || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = name[prefix.Length..].Replace("__", ":", StringComparison.Ordinal);
            result[path] = entry.Value?.ToString();
        }

        return result;
    }
}
