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
