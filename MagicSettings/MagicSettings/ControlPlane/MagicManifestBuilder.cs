namespace MagicSettings;

internal static class MagicManifestBuilder
{
    public static MagicSettingsSchemaManifest Build<TSettings>(MagicSettingsOptions<TSettings> options) where TSettings : class, new()
    {
        var discovered = new List<MagicSettingManifestEntry>();
        Visit(typeof(TSettings), string.Empty, discovered, new HashSet<Type>());
        var entries = discovered
            .Select(item => item with
            {
                Sensitive = item.Sensitive || options.SensitivePaths.Contains(item.Path),
                RemoteOverrideAllowed = item.RemoteOverrideAllowed && !string.Equals(
                    item.Path,
                    options.ControlPlane.Bootstrap.PersistentSettingPath,
                    StringComparison.OrdinalIgnoreCase)
            })
            .ToList();
        var canonical = string.Join("\n", entries.OrderBy(static item => item.Path, StringComparer.Ordinal).Select(static item => $"{item.Path}|{item.Type}|{item.Nullable}|{item.Sensitive}|{item.RemoteOverrideAllowed}"));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new(options.ApplicationId, options.ApplicationVersion, options.SchemaVersion, fingerprint, entries);
    }

    private static void Visit(Type type, string path, ICollection<MagicSettingManifestEntry> entries, ISet<Type> stack)
    {
        if (!stack.Add(type))
        {
            return;
        }

        try
        {
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(static property => property.CanRead))
            {
                var propertyPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}:{property.Name}";
                var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var sensitive = property.IsDefined(typeof(MagicSensitiveAttribute), inherit: true);
                var remoteAllowed = property.GetCustomAttribute<MagicRemoteOverrideAttribute>(inherit: true)?.Allowed ?? true;
                var description = property.GetCustomAttribute<MagicSettingDescriptionAttribute>(inherit: true)?.Description;
                if (IsLeaf(propertyType))
                {
                    entries.Add(new(
                        propertyPath,
                        propertyType.FullName ?? propertyType.Name,
                        Nullable.GetUnderlyingType(property.PropertyType) is not null || !property.PropertyType.IsValueType,
                        sensitive,
                        remoteAllowed,
                        description));
                }
                else if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType) || propertyType == typeof(string))
                {
                    Visit(propertyType, propertyPath, entries, stack);
                }
                else
                {
                    entries.Add(new(propertyPath, propertyType.FullName ?? propertyType.Name, true, sensitive, remoteAllowed, description));
                }
            }
        }
        finally
        {
            stack.Remove(type);
        }
    }

    private static bool IsLeaf(Type type)
        => type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) || type == typeof(Uri) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan);
}
