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
public sealed class MagicControlPlaneEndpointResolver : IMagicControlPlaneEndpointResolver
{
    public MagicResolvedControlPlaneEndpoint Resolve<TSettings>(
        MagicSettingsOptions<TSettings> options,
        JsonObject persistentDocument,
        Uri? runtimeOverride = null) where TSettings : class, new()
    {
        if (runtimeOverride is not null)
        {
            return new(Validate(runtimeOverride, "runtime override", options.ControlPlane.Bootstrap.AllowInsecureHttp), MagicControlPlaneEndpointSource.RuntimeOverride);
        }

        var environmentValue = Environment.GetEnvironmentVariable(options.ControlPlane.Bootstrap.EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return new(ParseAbsolute(environmentValue, options.ControlPlane.Bootstrap.EnvironmentVariableName, options.ControlPlane.Bootstrap.AllowInsecureHttp), MagicControlPlaneEndpointSource.EnvironmentVariable);
        }

        if (MagicJsonPath.TryGet(persistentDocument, options.ControlPlane.Bootstrap.PersistentSettingPath, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var persistentValue)
            && !string.IsNullOrWhiteSpace(persistentValue))
        {
            return new(ParseAbsolute(persistentValue, options.ControlPlane.Bootstrap.PersistentSettingPath, options.ControlPlane.Bootstrap.AllowInsecureHttp), MagicControlPlaneEndpointSource.PersistentSettings);
        }

        return options.ControlPlane.Bootstrap.CodeFallbackEndpoint is { } fallback
            ? new(Validate(fallback, "code fallback", options.ControlPlane.Bootstrap.AllowInsecureHttp), MagicControlPlaneEndpointSource.CodeFallback)
            : MagicResolvedControlPlaneEndpoint.None;
    }

    private static Uri ParseAbsolute(string value, string source, bool allowInsecureHttp)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException($"Control-plane endpoint from '{source}' must be an absolute URI.");
        }

        return Validate(endpoint, source, allowInsecureHttp);
    }

    internal static Uri Validate(Uri endpoint, string source, bool allowInsecureHttp)
    {
        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Control-plane endpoint from '{source}' must use HTTP or HTTPS.");
        }

        if (string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !allowInsecureHttp
            && !endpoint.IsLoopback)
        {
            throw new InvalidOperationException($"Control-plane endpoint from '{source}' must use HTTPS unless insecure HTTP is explicitly enabled or the endpoint is loopback.");
        }

        return endpoint;
    }
}

internal sealed class MagicSettingsControlPlane<TSettings> : IMagicSettingsControlPlane where TSettings : class, new()
{
    private readonly MagicSettingsOptions<TSettings> _options;
    private readonly MagicSettingsRuntime<TSettings> _runtime;
    private readonly IMagicNodeAuthenticator _authenticator;
    private readonly IMagicNodeIdentityManager _identityManager;
    private readonly IMagicControlPlaneTransport _transport;
    private readonly IMagicControlPlaneEndpointResolver _resolver;
    private readonly MagicSettingsSchemaManifest _manifest;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MagicControlPlaneTrust? _trust;
    private Uri? _runtimeOverrideEndpoint;
    private MagicSettingsMigrationReport? _pendingMigrationReport;
    private MagicIdentityContinuityProof? _pendingContinuityProof;
    private long _lastRevision;

    public MagicSettingsControlPlane(
        MagicSettingsOptions<TSettings> options,
        MagicSettingsRuntime<TSettings> runtime,
        IMagicNodeAuthenticator authenticator,
        IMagicNodeIdentityManager identityManager,
        IMagicControlPlaneTransport transport,
        IMagicControlPlaneEndpointResolver resolver,
        JsonObject persistentDocument,
        MagicSettingsMigrationReport? migrationReport)
    {
        _options = options;
        _runtime = runtime;
        _authenticator = authenticator;
        _identityManager = identityManager;
        _transport = transport;
        _resolver = resolver;
        _manifest = MagicManifestBuilder.Build(options);
        _pendingMigrationReport = migrationReport;
        CurrentEndpoint = resolver.Resolve(options, persistentDocument);
        State = CurrentEndpoint.Endpoint is null ? MagicControlPlaneState.Disabled : MagicControlPlaneState.Configured;
        _identityManager.IdentityChanged += OnIdentityChanged;
    }

    public MagicControlPlaneState State { get; private set; }
    public MagicResolvedControlPlaneEndpoint CurrentEndpoint { get; private set; }

    internal bool TryGetActiveConnection(out Uri endpoint, out MagicControlPlaneTrust trust)
    {
        if (State == MagicControlPlaneState.Active && CurrentEndpoint.Endpoint is { } current && _trust is { } currentTrust)
        {
            endpoint = current;
            trust = currentTrust;
            return true;
        }

        endpoint = null!;
        trust = null!;
        return false;
    }

    public async ValueTask BootstrapAsync(CancellationToken cancellationToken)
    {
        if (!_options.ControlPlane.Bootstrap.ConnectOnStartup || CurrentEndpoint.Endpoint is null)
        {
            return;
        }

        var trust = _options.ControlPlane.Bootstrap.Trust
            ?? throw new InvalidOperationException("ConnectOnStartup requires an explicit MagicControlPlaneTrust configuration.");
        await ConfigureResolvedAsync(CurrentEndpoint, trust, cancellationToken);
    }

    public async ValueTask ConfigureAsync(Uri endpoint, MagicControlPlaneTrust trust, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(trust);
        endpoint = MagicControlPlaneEndpointResolver.Validate(endpoint, "runtime override", _options.ControlPlane.Bootstrap.AllowInsecureHttp);
        var resolved = new MagicResolvedControlPlaneEndpoint(endpoint, MagicControlPlaneEndpointSource.RuntimeOverride);
        await ConfigureResolvedAsync(resolved, trust, cancellationToken);
        _runtimeOverrideEndpoint = endpoint;
    }

    public async ValueTask DisconnectAsync(bool clearRemoteOverrides = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            State = MagicControlPlaneState.Disconnected;
            _trust = null;
            if (clearRemoteOverrides)
            {
                _lastRevision = 0;
                _runtime.ClearRemote();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (CurrentEndpoint.Endpoint is null || _trust is null)
            {
                return;
            }

            await SynchronizeUnderLockAsync(CurrentEndpoint, _trust, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ReevaluatePersistentEndpointAsync(JsonObject persistentDocument, CancellationToken cancellationToken)
    {
        if (!_options.ControlPlane.Bootstrap.WatchPersistentEndpoint)
        {
            return;
        }

        var resolved = _resolver.Resolve(_options, persistentDocument, _runtimeOverrideEndpoint);
        if (Equals(resolved.Endpoint, CurrentEndpoint.Endpoint))
        {
            return;
        }

        if (resolved.Endpoint is null)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                CurrentEndpoint = MagicResolvedControlPlaneEndpoint.None;
                _trust = null;
                State = MagicControlPlaneState.Disabled;
                if (!_options.ControlPlane.KeepLastKnownGoodDuringOutage)
                {
                    _lastRevision = 0;
                    _runtime.ClearRemote();
                }
            }
            finally
            {
                _gate.Release();
            }
            return;
        }

        var trust = _options.ControlPlane.Bootstrap.Trust;
        if (trust is null)
        {
            CurrentEndpoint = resolved;
            State = MagicControlPlaneState.Configured;
            return;
        }

        await ConfigureResolvedAsync(resolved, trust, cancellationToken);
    }

    private async ValueTask ConfigureResolvedAsync(MagicResolvedControlPlaneEndpoint resolved, MagicControlPlaneTrust trust, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var previousEndpoint = CurrentEndpoint;
            var previousTrust = _trust;
            State = MagicControlPlaneState.Connecting;
            try
            {
                await SynchronizeUnderLockAsync(resolved, trust, cancellationToken);
                CurrentEndpoint = resolved;
                _trust = trust;
            }
            catch
            {
                CurrentEndpoint = previousEndpoint;
                _trust = previousTrust;
                State = MagicControlPlaneState.Faulted;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask SynchronizeUnderLockAsync(MagicResolvedControlPlaneEndpoint resolved, MagicControlPlaneTrust trust, CancellationToken cancellationToken)
    {
        var endpoint = resolved.Endpoint ?? throw new InvalidOperationException("A control-plane endpoint is required.");
        var syncUri = new Uri(endpoint, "magicsettings/sync");
        var identity = await _authenticator.GetCurrentIdentityAsync(cancellationToken);
        var signedPayloadHash = MagicSettingsSyncProof.ComputeBodySha256(
            identity,
            _manifest,
            _lastRevision,
            _pendingMigrationReport,
            _pendingContinuityProof);
        var proof = await _authenticator.CreateProofAsync(
            new(trust.AuthorityId, "POST", syncUri, signedPayloadHash),
            cancellationToken);
        var response = await _transport.SynchronizeAsync(
            endpoint,
            trust,
            new(identity, proof, _manifest, _lastRevision, _pendingMigrationReport, _pendingContinuityProof),
            cancellationToken);

        State = response.State;
        if (response.State == MagicControlPlaneState.Active)
        {
            _runtime.ApplyRemoteSnapshot(response.Snapshot, DateTimeOffset.UtcNow);
            _lastRevision = response.Snapshot.Revision;
            _pendingMigrationReport = null;
            _pendingContinuityProof = null;
        }
    }

    private void OnIdentityChanged(object? sender, MagicIdentityChange change)
    {
        _lastRevision = 0;
        _pendingContinuityProof = change.ContinuityProof;
        if (change.Kind == MagicIdentityChangeKind.Reset || change.Kind == MagicIdentityChangeKind.RecoveredAfterLoss)
        {
            _runtime.ClearRemote();
        }
        State = MagicControlPlaneState.PendingApproval;
    }
}

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
}
