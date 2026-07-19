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
public interface IMagicSettings<out TSettings> where TSettings : class
{
    TSettings Current { get; }
    long Revision { get; }
    event EventHandler<MagicSettingsChangedEventArgs<TSettings>>? Changed;
    MagicSettingExplanation Explain(string path);
}

public sealed record MagicSettingsChangedEventArgs<TSettings>(TSettings Previous, TSettings Current, long Revision) where TSettings : class;

public sealed record MagicSettingExplanation(
    string Path,
    string? EffectiveValue,
    string EffectiveSource,
    IReadOnlyList<MagicSettingSourceValue> Sources,
    bool IsSensitive);

public sealed record MagicSettingSourceValue(string Source, bool Present, string? Value);

public interface IMagicSettingsValidator<in TSettings> where TSettings : class
{
    ValueTask<IReadOnlyList<string>> ValidateAsync(TSettings settings, CancellationToken cancellationToken = default);
}

public interface IMagicSettingsSourceProvider
{
    string Name { get; }
    int Priority { get; }
    ValueTask<IReadOnlyDictionary<string, string?>> LoadAsync(CancellationToken cancellationToken = default);
}

public interface IMagicSettingsMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    void Apply(JsonObject document, MagicMigrationContext context);
}

public interface IMagicSecretProvider
{
    ValueTask<MagicSecretLease<T>> GetAsync<T>(string name, CancellationToken cancellationToken = default);
}

public sealed class MagicSecretLease<T> : IAsyncDisposable
{
    public MagicSecretLease(T value, DateTimeOffset? expiresUtc = null)
    {
        Value = value;
        ExpiresUtc = expiresUtc;
    }

    public T Value { get; }
    public DateTimeOffset? ExpiresUtc { get; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public interface IMagicControlPlaneEndpointResolver
{
    MagicResolvedControlPlaneEndpoint Resolve<TSettings>(
        MagicSettingsOptions<TSettings> options,
        JsonObject persistentDocument,
        Uri? runtimeOverride = null) where TSettings : class, new();
}

public interface IMagicSettingsControlPlane
{
    MagicControlPlaneState State { get; }
    MagicResolvedControlPlaneEndpoint CurrentEndpoint { get; }
    ValueTask ConfigureAsync(Uri endpoint, MagicControlPlaneTrust trust, CancellationToken cancellationToken = default);
    ValueTask DisconnectAsync(bool clearRemoteOverrides = false, CancellationToken cancellationToken = default);
    ValueTask RefreshAsync(CancellationToken cancellationToken = default);
}

public interface IMagicControlPlaneTransport
{
    ValueTask<MagicSettingsSyncResponse> SynchronizeAsync(
        Uri endpoint,
        MagicControlPlaneTrust trust,
        MagicSettingsSyncRequest request,
        CancellationToken cancellationToken = default);
}


public interface IMagicSecretTransport
{
    ValueTask<MagicSecretResponse> ResolveSecretAsync(
        Uri endpoint,
        MagicControlPlaneTrust trust,
        MagicSecretRequest request,
        CancellationToken cancellationToken = default);
}

public interface IMagicNodeAuthenticator
{
    ValueTask<MagicNodeIdentityDescriptor> GetCurrentIdentityAsync(CancellationToken cancellationToken = default);
    ValueTask<MagicAuthenticationProof> CreateProofAsync(MagicAuthenticationRequest request, CancellationToken cancellationToken = default);
}

public interface IMagicNodeIdentityManager
{
    event EventHandler<MagicIdentityChange>? IdentityChanged;
    ValueTask<MagicNodeIdentityDescriptor> GetCurrentAsync(CancellationToken cancellationToken = default);
    ValueTask<MagicIdentityChange> RotateAsync(string reason, CancellationToken cancellationToken = default);
    ValueTask<MagicIdentityChange> ResetAsync(MagicIdentityResetRequest request, CancellationToken cancellationToken = default);
}

public interface IMagicNodeIdentityStore
{
    ValueTask<MagicStoredNodeIdentity?> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsync(MagicStoredNodeIdentity identity, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(CancellationToken cancellationToken = default);
}

public sealed record MagicStoredNodeIdentity(
    Guid NodeId,
    Guid CredentialId,
    string PublicKey,
    string PrivateKey,
    DateTimeOffset CreatedUtc);
}
namespace MagicSettings
{
[AttributeUsage(AttributeTargets.Property)]
public sealed class MagicSensitiveAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class MagicRemoteOverrideAttribute : Attribute
{
    public MagicRemoteOverrideAttribute(bool allowed = true) => Allowed = allowed;
    public bool Allowed { get; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class MagicSettingDescriptionAttribute : Attribute
{
    public MagicSettingDescriptionAttribute(string description) => Description = description;
    public string Description { get; }
}
}
