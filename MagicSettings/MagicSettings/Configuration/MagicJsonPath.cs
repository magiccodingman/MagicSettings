namespace MagicSettings;

internal static class MagicJsonPath
{
    public static bool TryGet(JsonObject root, string path, out JsonNode? value)
    {
        value = root;
        foreach (var segment in Split(path))
        {
            if (value is not JsonObject obj || !TryGetProperty(obj, segment, out _, out value))
            {
                value = null;
                return false;
            }
        }

        return true;
    }

    public static void Set(JsonObject root, string path, JsonNode? value)
    {
        var segments = Split(path);
        if (segments.Length == 0)
        {
            throw new ArgumentException("A setting path is required.", nameof(path));
        }

        var current = root;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            var segment = segments[index];
            if (!TryGetProperty(current, segment, out var actualName, out var existing) || existing is not JsonObject child)
            {
                child = new JsonObject();
                current[actualName ?? segment] = child;
            }

            current = child;
        }

        if (TryGetProperty(current, segments[^1], out var finalName, out _))
        {
            current[finalName!] = value?.DeepClone();
        }
        else
        {
            current[segments[^1]] = value?.DeepClone();
        }
    }

    public static bool TryTake(JsonObject root, string path, out JsonNode? value)
    {
        value = null;
        var segments = Split(path);
        if (segments.Length == 0)
        {
            return false;
        }

        var current = root;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (!TryGetProperty(current, segments[index], out _, out var existing) || existing is not JsonObject child)
            {
                return false;
            }

            current = child;
        }

        if (!TryGetProperty(current, segments[^1], out var actualName, out value))
        {
            return false;
        }

        current.Remove(actualName!);
        return true;
    }

    public static bool TryGetProperty(JsonObject obj, string name, out string? actualName, out JsonNode? value)
    {
        if (obj.TryGetPropertyValue(name, out value))
        {
            actualName = name;
            return true;
        }

        foreach (var pair in obj)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                actualName = pair.Key;
                value = pair.Value;
                return true;
            }
        }

        actualName = null;
        value = null;
        return false;
    }

    public static string[] Split(string path)
        => path.Split(new[] { ':', '.' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
