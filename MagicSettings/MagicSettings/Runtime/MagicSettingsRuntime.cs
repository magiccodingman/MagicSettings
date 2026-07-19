namespace MagicSettings;

internal sealed class MagicSettingsRuntime<TSettings> : IMagicSettings<TSettings> where TSettings : class, new()
{
    private readonly object _gate = new();
    private readonly MagicSettingsOptions<TSettings> _options;
    private readonly MagicSettingsConfigurationProvider _configurationProvider;
    private readonly string _settingsPath;
    private Dictionary<string, string?> _persistent = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string?> _environment = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string?> _remote = new(StringComparer.OrdinalIgnoreCase);
    private MagicRemoteSnapshot _remoteSnapshot = MagicRemoteSnapshot.Empty;
    private Dictionary<string, string?> _providers = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string?> _effective = new(StringComparer.OrdinalIgnoreCase);
    private TSettings _current = new();
    private long _revision;

    public MagicSettingsRuntime(MagicSettingsOptions<TSettings> options, MagicSettingsConfigurationProvider configurationProvider, string settingsPath)
    {
        _options = options;
        _configurationProvider = configurationProvider;
        _settingsPath = settingsPath;
    }

    public TSettings Current { get { lock (_gate) return _current; } }
    public long Revision { get { lock (_gate) return _revision; } }
    public event EventHandler<MagicSettingsChangedEventArgs<TSettings>>? Changed;

    public async ValueTask InitializeAsync(JsonObject document, CancellationToken cancellationToken)
    {
        var persistent = MagicJsonFlattener.Flatten(document);
        var environment = MagicEnvironmentOverrides.Read(_options.EnvironmentOverridePrefix);
        var providers = await LoadCustomProvidersAsync(cancellationToken);
        PublishCandidate(persistent: persistent, environment: environment, providers: providers);
    }

    public async ValueTask ReloadPersistentAsync(CancellationToken cancellationToken)
    {
        var document = await MagicSettingsDocumentStore.ReadAsync(_settingsPath, cancellationToken);
        var persistent = MagicJsonFlattener.Flatten(document);
        var providers = await LoadCustomProvidersAsync(cancellationToken);
        PublishCandidate(persistent: persistent, providers: providers);
    }

    public void RefreshEnvironment()
        => PublishCandidate(environment: MagicEnvironmentOverrides.Read(_options.EnvironmentOverridePrefix));

    public void ApplyRemoteSnapshot(MagicRemoteSnapshot snapshot, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var values = MaterializeRemote(snapshot, nowUtc);
        PublishCandidate(remote: values, remoteSnapshot: snapshot, updateRemoteSnapshot: true);
    }

    public void RefreshRemoteExpirations(DateTimeOffset nowUtc)
    {
        MagicRemoteSnapshot snapshot;
        Dictionary<string, string?> current;
        lock (_gate)
        {
            snapshot = _remoteSnapshot;
            current = _remote;
        }

        var values = MaterializeRemote(snapshot, nowUtc);
        if (!DictionaryEquals(current, values))
        {
            PublishCandidate(remote: values);
        }
    }

    public void ClearRemote()
    {
        PublishCandidate(
            remote: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            remoteSnapshot: MagicRemoteSnapshot.Empty,
            updateRemoteSnapshot: true);
    }

    public MagicSettingExplanation Explain(string path)
    {
        lock (_gate)
        {
            var sensitive = _options.SensitivePaths.Contains(path);
            var sources = new[]
            {
                SourceValue("Remote", _remote, path, sensitive),
                SourceValue("Environment", _environment, path, sensitive),
                SourceValue("CustomProviders", _providers, path, sensitive),
                SourceValue("PersistentFile", _persistent, path, sensitive)
            };
            _effective.TryGetValue(path, out var effective);
            var source = sources.FirstOrDefault(static item => item.Present)?.Source ?? "Missing";
            return new(path, sensitive && effective is not null ? "[REDACTED]" : effective, source, sources, sensitive);
        }
    }

    private async ValueTask<Dictionary<string, string?>> LoadCustomProvidersAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in _options.Providers.OrderBy(static item => item.Priority))
        {
            var values = await provider.LoadAsync(cancellationToken);
            foreach (var pair in values)
            {
                result[pair.Key] = pair.Value;
            }
        }

        return result;
    }

    private void PublishCandidate(
        Dictionary<string, string?>? persistent = null,
        Dictionary<string, string?>? providers = null,
        Dictionary<string, string?>? environment = null,
        Dictionary<string, string?>? remote = null,
        MagicRemoteSnapshot? remoteSnapshot = null,
        bool updateRemoteSnapshot = false)
    {
        TSettings previous;
        TSettings current;
        long revision;
        Dictionary<string, string?> effective;
        lock (_gate)
        {
            var nextPersistent = persistent ?? _persistent;
            var nextProviders = providers ?? _providers;
            var nextEnvironment = environment ?? _environment;
            var nextRemote = remote ?? _remote;

            effective = new Dictionary<string, string?>(nextPersistent, StringComparer.OrdinalIgnoreCase);
            Merge(effective, nextProviders);
            Merge(effective, nextEnvironment);
            Merge(effective, nextRemote);

            var builder = new ConfigurationBuilder().AddInMemoryCollection(effective);
            current = builder.Build().Get<TSettings>() ?? new TSettings();
            Validate(current);

            previous = _current;
            _persistent = nextPersistent;
            _providers = nextProviders;
            _environment = nextEnvironment;
            _remote = nextRemote;
            if (updateRemoteSnapshot)
            {
                _remoteSnapshot = remoteSnapshot ?? MagicRemoteSnapshot.Empty;
            }
            _current = current;
            _effective = effective;
            revision = ++_revision;
        }

        _configurationProvider.Publish(effective);
        Changed?.Invoke(this, new(previous, current, revision));
    }

    private void Validate(TSettings settings)
    {
        var dataAnnotationFailures = new List<string>();
        ValidateDataAnnotations(settings, string.Empty, dataAnnotationFailures, new HashSet<object>(ReferenceEqualityComparer.Instance));
        if (dataAnnotationFailures.Count > 0)
        {
            throw new InvalidOperationException($"MagicSettings validation failed: {string.Join("; ", dataAnnotationFailures)}");
        }

        foreach (var validator in _options.Validators)
        {
            var failures = validator.ValidateAsync(settings).AsTask().GetAwaiter().GetResult();
            if (failures.Count > 0)
            {
                throw new InvalidOperationException($"MagicSettings validation failed: {string.Join("; ", failures)}");
            }
        }
    }

    private static void ValidateDataAnnotations(
        object instance,
        string path,
        ICollection<string> failures,
        ISet<object> visited)
    {
        if (!visited.Add(instance))
        {
            return;
        }

        var results = new List<ValidationResult>();
        var context = new ValidationContext(instance);
        if (!Validator.TryValidateObject(instance, context, results, validateAllProperties: true))
        {
            foreach (var result in results)
            {
                var members = result.MemberNames.Any()
                    ? string.Join(",", result.MemberNames.Select(member => string.IsNullOrEmpty(path) ? member : $"{path}:{member}"))
                    : path;
                failures.Add(string.IsNullOrEmpty(members)
                    ? result.ErrorMessage ?? "Validation failed."
                    : $"{members}: {result.ErrorMessage ?? "Validation failed."}");
            }
        }

        foreach (var property in instance.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            var value = property.GetValue(instance);
            if (value is null || IsSimpleValidationType(value.GetType()))
            {
                continue;
            }

            var childPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}:{property.Name}";
            if (value is IEnumerable sequence)
            {
                var index = 0;
                foreach (var item in sequence)
                {
                    if (item is not null && item is not string && !item.GetType().IsValueType)
                    {
                        ValidateDataAnnotations(item, $"{childPath}:{index}", failures, visited);
                    }
                    index++;
                }
            }
            else
            {
                ValidateDataAnnotations(value, childPath, failures, visited);
            }
        }
    }

    private static bool IsSimpleValidationType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive
               || type.IsEnum
               || type.IsValueType
               || type == typeof(string)
               || type == typeof(Uri)
               || type == typeof(Version);
    }

    private static void Merge(IDictionary<string, string?> target, IReadOnlyDictionary<string, string?> source)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private Dictionary<string, string?> MaterializeRemote(MagicRemoteSnapshot snapshot, DateTimeOffset nowUtc)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in snapshot.Values)
        {
            if (string.Equals(
                    pair.Key,
                    _options.ControlPlane.Bootstrap.PersistentSettingPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = pair.Value;
            if (value.Durability == MagicRemoteValueDurability.Expiring && value.ExpiresUtc is { } expires && expires <= nowUtc)
            {
                continue;
            }

            values[pair.Key] = value.State == MagicValueState.Null
                ? null
                : value.Value is { } element ? ElementToString(element) : null;
        }
        return values;
    }

    private static bool DictionaryEquals(
        IReadOnlyDictionary<string, string?> left,
        IReadOnlyDictionary<string, string?> right)
        => left.Count == right.Count
           && left.All(pair => right.TryGetValue(pair.Key, out var value)
                               && string.Equals(pair.Value, value, StringComparison.Ordinal));

    private static MagicSettingSourceValue SourceValue(string source, IReadOnlyDictionary<string, string?> values, string path, bool sensitive)
    {
        var present = values.TryGetValue(path, out var value);
        return new(source, present, sensitive && value is not null ? "[REDACTED]" : value);
    }

    private static string? ElementToString(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null => null,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.GetRawText()
        };
}
