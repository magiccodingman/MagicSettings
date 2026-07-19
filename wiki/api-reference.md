# API reference

This reference describes the public MagicSettings API that application developers, control-plane implementers, and AI coding agents are expected to use. It intentionally focuses on behavior, ownership, persistence, security boundaries, and common call patterns—not merely symbol names.

## Package map

| Package | Purpose |
|---|---|
| `MagicSettings` | Application runtime, initialization, configuration composition, migrations, endpoint bootstrap, identity storage, request signing, HTTP helpers, and secret clients. |
| `MagicSettings.Share` | Protocol records and enums shared by clients, APIs, and control-plane implementations. |
| `MagicSettings.Server` | Storage-agnostic proof verification, enrollment, credential lifecycle, synchronization, replay defense, and secret-resolution helpers. |

## Normal application entry points

Most applications only need these APIs:

- `AddMagicSettingsAsync<TSettings>` during host construction.
- `IMagicSettings<TSettings>` to inspect the typed effective snapshot and provenance.
- `IOptions<TSettings>` or `IOptionsMonitor<TSettings>` for standard .NET consumption.
- `IMagicSettingsControlPlane` when the endpoint is discovered or changed at runtime.
- `IMagicNodeAuthenticator` to authenticate the node to another API.
- `AddMagicNodeAuthentication` for normal `HttpClient` integration.
- `IMagicNodeIdentityManager` for intentional credential rotation or destructive reset.
- `IMagicSecretProvider` for explicit asynchronous secret retrieval.

Protocol DTOs and server helpers are normally used by infrastructure libraries, API middleware, or a control-plane implementation rather than ordinary business code.

---

# MagicSettings package

## Initialization

### `MagicSettingsInitializationExtensions.AddMagicSettingsAsync<TSettings>`

```csharp
ValueTask<MagicSettingsInitializationResult> AddMagicSettingsAsync<TSettings>(
    this IHostApplicationBuilder builder,
    string[]? args = null,
    Action<MagicSettingsOptions<TSettings>>? configure = null,
    CancellationToken cancellationToken = default)
    where TSettings : class, new();
```

Primary initialization method. Call it once, immediately after creating the host builder.

It:

1. Resolves the environment and settings path.
2. Applies the selected code-defined environment profile to the template.
3. Generates or reconciles the persistent JSON file.
4. Runs required sequential migrations.
5. Creates or loads the node identity.
6. Builds the initial effective settings snapshot.
7. Adds MagicSettings to `IConfiguration`.
8. Registers the typed runtime, options support, identity services, control-plane services, hosted reload service, and—when supported by the selected transport—the secret provider.

The method may throw when the persistent document is malformed, a migration chain is incomplete, validation fails, a strict array policy is missing, or security-sensitive bootstrap configuration is invalid.

### `MagicSettingsInitializationResult`

| Property | Meaning |
|---|---|
| `ShouldExit` | `true` when a MagicSettings command-line operation completed and the host should not start. |
| `ExitCode` | Exit code for the command-line operation. |
| `SettingsPath` | Fully resolved persistent settings file path. |
| `Environment` | Effective MagicSettings environment. |

### `MagicSettingsRuntimeRegistration`

Contains the resolved `SettingsPath` and is registered in dependency injection for infrastructure code that needs the runtime file location.

## Runtime settings access

### `IMagicSettings<TSettings>`

```csharp
TSettings Current { get; }
long Revision { get; }
event EventHandler<MagicSettingsChangedEventArgs<TSettings>>? Changed;
MagicSettingExplanation Explain(string path);
```

- `Current` returns the latest validated typed snapshot. Treat the returned object as read-only application state.
- `Revision` increments whenever a candidate snapshot is successfully published.
- `Changed` fires after a new validated snapshot becomes active.
- `Explain(path)` reports which provider supplied a path and which value won.

Failed reloads do not replace `Current`; the last known good snapshot remains active.

### `MagicSettingsChangedEventArgs<TSettings>`

Contains `Previous`, `Current`, and the newly published `Revision`.

### `MagicSettingExplanation`

| Property | Meaning |
|---|---|
| `Path` | Requested configuration path. |
| `EffectiveValue` | Winning value, redacted when sensitive. |
| `EffectiveSource` | Winning provider name or `Missing`. |
| `Sources` | Per-provider presence and value information. |
| `IsSensitive` | Whether diagnostics must redact the value. |

### `MagicSettingSourceValue`

Describes one provider contribution through `Source`, `Present`, and `Value`.

## Main options

### `MagicSettingsOptions<TSettings>`

| Property | Purpose |
|---|---|
| `ApplicationId` | Stable logical application identifier sent in schema manifests and remote-record keys. |
| `ApplicationVersion` | Application version reported to the control plane. |
| `SchemaVersion` | Current local settings schema version. Increase it only with a complete migration chain. |
| `Template` | Code-defined settings defaults used to generate and reconcile the persistent file. |
| `Path` | Explicit JSON file or directory path. Overrides `MAGICSETTINGS_PATH`. |
| `FileName` | File name appended when the selected path is a directory. Defaults to `appsettings.json`. |
| `EnvironmentOverridePrefix` | Prefix for OS/process environment overrides. Defaults to `MagicSettings__`. |
| `Environment` | Explicit environment name; when empty, standard environment sources are resolved. |
| `ReloadOnChange` | Enables persistent-file change detection. |
| `ReloadDebounce` | Delay used to avoid reading a partially written settings file. |
| `PreserveUnknownProperties` | Declares the intended non-destructive behavior for properties no longer known by the current template. |
| `IdentityFileName` | Default identity filename when the identity path is a directory. |
| `IdentityPath` | Explicit identity file or directory path. Overrides `MAGICSETTINGS_IDENTITY_PATH`. |
| `Json` | Serializer options used for settings documents and related serialization. |
| `Failures` | Failure-policy configuration. |
| `ControlPlane` | Bootstrap, polling, and outage behavior. |
| `EnvironmentProfiles` | Environment-specific mutations applied to the template before reconciliation. Prefer `ConfigureEnvironment`. |
| `Migrations` | Ordered migration implementations. |
| `Validators` | Additional typed validators. |
| `Providers` | Custom bulk configuration providers. |
| `ArrayPolicies` | Per-path array reconciliation policy. |
| `SensitivePaths` | Paths redacted from provenance diagnostics. |
| `ControlPlaneTransport` | Optional custom synchronization transport. |
| `ControlPlaneEndpointResolver` | Optional custom local bootstrap resolver. |
| `IdentityStore` | Optional OS-, vault-, or hardware-backed identity store. |

### `ConfigureEnvironment`

```csharp
void ConfigureEnvironment(string environment, Action<TSettings> configure);
```

Registers a template mutation for one environment. The mutation runs before the persistent file is generated or reconciled. It does not become a hidden runtime override.

### `MagicSettingsFailurePolicy`

| Property | Purpose |
|---|---|
| `StrictDevelopmentMode` | Makes ambiguous development/local/test behavior fail early. |
| `AmbiguousArrayInProduction` | Intended production response to ambiguous array reconciliation. |
| `RuntimeReloadFailure` | Intended response when a changed file cannot produce a valid snapshot. |

### `MagicFailureAction`

- `StopStartup`
- `WarnAndContinue`
- `KeepLastKnownGood`

### `MagicArrayMergePolicy`

- `PreserveExisting`: keep the operator-managed array unchanged.
- `ReplaceWithTemplate`: replace the persistent array with the code template.
- `AppendMissing`: append template items not already present.
- `Union`: retain unique existing items and add unique template items.

Do not assume arrays are sets unless their semantics genuinely are set-like.

## Validation, providers, and migrations

### `IMagicSettingsValidator<TSettings>`

```csharp
ValueTask<IReadOnlyList<string>> ValidateAsync(
    TSettings settings,
    CancellationToken cancellationToken = default);
```

Returns validation failures. An empty list means success. Validation occurs before a candidate snapshot is published.

### `IMagicSettingsSourceProvider`

```csharp
string Name { get; }
int Priority { get; }
ValueTask<IReadOnlyDictionary<string, string?>> LoadAsync(
    CancellationToken cancellationToken = default);
```

Implements a complete bulk provider snapshot. Providers are applied after the persistent file and before OS environment and remote values. Higher-priority providers are applied later among custom providers.

Use this for mounted files, a local database, a vault cache, or another source that can return a complete path/value snapshot. Use `IMagicSecretProvider` instead for on-demand secrets.

### `IMagicSettingsMigration`

```csharp
int FromVersion { get; }
int ToVersion { get; }
void Apply(JsonObject document, MagicMigrationContext context);
```

Each migration must advance the version. Missing steps, downgrade attempts, and non-advancing migrations stop initialization.

### `MagicMigrationContext`

| Member | Purpose |
|---|---|
| `SafeOperations` | Operations safe to report as automatically applied. |
| `ReviewItems` | Remote or destructive effects requiring administrative review. |
| `RecordSafe(operation)` | Adds an informational safe-operation record. |
| `RequireReview(...)` | Adds an explicit migration review item. |
| `Rename(document, from, to, remoteSafeProjection)` | Renames a local path and optionally records a remote review requirement. |
| `Transform(document, path, transform, description, remoteSafeProjection)` | Transforms a local value and records review unless explicitly marked remotely safe. |
| `SetIfMissing(document, path, value)` | Adds a value only when absent. |
| `Remove(document, path, reason)` | Removes locally and always creates a destructive remote review item. |

## Attributes

### `MagicSensitiveAttribute`

Marks a settings property as sensitive in the generated schema manifest. Sensitive values are redacted from `Explain` output.

### `MagicRemoteOverrideAttribute`

```csharp
[MagicRemoteOverride(false)]
```

Controls whether the control plane may supply a value for a property. The control-plane endpoint path is additionally blocked regardless of ordinary remote settings.

### `MagicSettingDescriptionAttribute`

Adds human-readable schema metadata for a property.

## Path and environment helpers

### `MagicSettingsEnvironmentResolver.Resolve`

Resolves the environment in this order: explicit value, `MAGICSETTINGS_ENVIRONMENT`, `DOTNET_ENVIRONMENT`, `ASPNETCORE_ENVIRONMENT`, host environment, then `Production`.

### `MagicSettingsPathResolver.Resolve<TSettings>`

Resolves `options.Path`, then `MAGICSETTINGS_PATH`, then `AppContext.BaseDirectory/options.FileName`. A `.json` path is treated as a file; another path is treated as a directory.

### `MagicIdentityPathResolver.Resolve`

Resolves the explicit identity path, then `MAGICSETTINGS_IDENTITY_PATH`, then a file adjacent to the settings document.

## Control-plane bootstrap and lifecycle

### `MagicControlPlaneBootstrapOptions`

| Property | Purpose |
|---|---|
| `EnvironmentVariableName` | Dedicated bootstrap endpoint variable. Default: `MAGICSETTINGS_CONTROL_PLANE_ENDPOINT`. |
| `PersistentSettingPath` | Path read from the persistent local JSON document. Default: `MagicSettings:ControlPlane:Endpoint`. |
| `CodeFallbackEndpoint` | Final code-defined fallback endpoint. |
| `Trust` | Trust policy used for startup and watched local endpoint changes. |
| `ConnectOnStartup` | Attempts synchronization during hosted-service startup. |
| `WatchPersistentEndpoint` | Re-evaluates the persistent endpoint after a valid file reload. |
| `AllowInsecureHttp` | Allows non-loopback HTTP. This should almost never be enabled. |

Resolution precedence is runtime override, dedicated environment variable, persistent local document, code fallback, then disabled. Remote effective settings never participate.

### `MagicControlPlaneOptions`

| Property | Purpose |
|---|---|
| `Bootstrap` | Endpoint discovery and trust configuration. |
| `PollInterval` | Base synchronization interval. |
| `PollJitter` | Random variation to avoid synchronized fleet polling. |
| `KeepLastKnownGoodDuringOutage` | Retains the last valid remote layer when synchronization fails. |

### `IMagicSettingsControlPlane`

```csharp
MagicControlPlaneState State { get; }
MagicResolvedControlPlaneEndpoint CurrentEndpoint { get; }
ValueTask ConfigureAsync(Uri endpoint, MagicControlPlaneTrust trust, CancellationToken cancellationToken = default);
ValueTask DisconnectAsync(bool clearRemoteOverrides = false, CancellationToken cancellationToken = default);
ValueTask RefreshAsync(CancellationToken cancellationToken = default);
```

- `ConfigureAsync` authenticates and synchronizes with a runtime-selected endpoint. A failed transition does not accept values from the new endpoint.
- `DisconnectAsync(false)` stops using the connection while preserving the last remote snapshot.
- `DisconnectAsync(true)` also clears the remote layer and reveals lower-priority values.
- `RefreshAsync` performs an immediate client-initiated synchronization when configured.

### `IMagicControlPlaneEndpointResolver`

Customizes local endpoint resolution. Implementations receive the persistent document—not the final effective snapshot—to prevent remote self-redirection.

### `MagicControlPlaneEndpointResolver`

Default resolver implementing the documented local-only precedence and HTTPS validation.

### `IMagicControlPlaneTransport`

```csharp
ValueTask<MagicSettingsSyncResponse> SynchronizeAsync(
    Uri endpoint,
    MagicControlPlaneTrust trust,
    MagicSettingsSyncRequest request,
    CancellationToken cancellationToken = default);
```

Extension point for non-HTTP transports or custom HTTP behavior. The transport must preserve request integrity and enforce the supplied trust policy.

### `HttpMagicControlPlaneTransport`

Default HTTP transport. It posts to `magicsettings/sync`, uses the `MagicNode` authorization scheme, supports system TLS or public-key fingerprint pinning, disables redirects, and can also resolve secrets.

## Node identity and authentication

### `IMagicNodeAuthenticator`

```csharp
ValueTask<MagicNodeIdentityDescriptor> GetCurrentIdentityAsync(CancellationToken cancellationToken = default);
ValueTask<MagicAuthenticationProof> CreateProofAsync(MagicAuthenticationRequest request, CancellationToken cancellationToken = default);
```

`GetCurrentIdentityAsync` returns public identity metadata only. `CreateProofAsync` signs one short-lived, audience-bound request. It does not expose the private key.

### `IMagicNodeIdentityManager`

```csharp
event EventHandler<MagicIdentityChange>? IdentityChanged;
ValueTask<MagicNodeIdentityDescriptor> GetCurrentAsync(CancellationToken cancellationToken = default);
ValueTask<MagicIdentityChange> RotateAsync(string reason, CancellationToken cancellationToken = default);
ValueTask<MagicIdentityChange> ResetAsync(MagicIdentityResetRequest request, CancellationToken cancellationToken = default);
```

- `RotateAsync` retains the logical node ID, generates a new credential, and produces a continuity proof signed by the old credential.
- `ResetAsync` creates a new node ID and credential. It requires `ConfirmDestructiveReset=true`, clears remote trust continuity, and requires enrollment approval again.
- `IdentityChanged` allows the synchronization layer and application telemetry to react to lifecycle changes.

Rotation is not a substitute for revocation after compromise. A stolen old key remains usable until the authorization system revokes it and relying APIs refresh their caches.

### `MagicNodeIdentityManager`

Default implementation of both `IMagicNodeIdentityManager` and `IMagicNodeAuthenticator` using ECDSA P-256 credentials.

Proof lifetimes must be greater than zero and no longer than five minutes; the default is one minute.

### `IMagicNodeIdentityStore`

```csharp
ValueTask<MagicStoredNodeIdentity?> LoadAsync(CancellationToken cancellationToken = default);
ValueTask SaveAsync(MagicStoredNodeIdentity identity, CancellationToken cancellationToken = default);
ValueTask DeleteAsync(CancellationToken cancellationToken = default);
```

Security-sensitive extension point. A custom implementation may use DPAPI, Keychain, TPM, HSM, Kubernetes, or another non-exportable key facility.

### `FileMagicNodeIdentityStore`

Portable file implementation. It writes atomically and restricts Unix permissions to the current user, but it is not equivalent to a hardware-backed vault.

### `MagicStoredNodeIdentity`

Contains the node ID, credential ID, public key, **private key**, and creation time. This record exists for identity-store implementations and must never be logged, returned by an API, or transmitted to a server.

### `MagicNodeAuthenticationHandler`

`DelegatingHandler` that buffers the outbound request body, computes its SHA-256, creates a fresh proof, and sends:

```text
Authorization: MagicNode <encoded proof>
X-Magic-Node-Id: <node ID>
X-Magic-Credential-Id: <credential ID>
```

### `MagicHttpClientBuilderExtensions.AddMagicNodeAuthentication`

```csharp
services.AddHttpClient<InventoryClient>()
    .AddMagicNodeAuthentication("Inventory.Api");
```

Adds request-bound node authentication to a typed or named `HttpClient`. The audience must exactly match what the receiving API expects.

### `MagicHash`

- `Sha256Hex(ReadOnlySpan<byte>)` returns lowercase SHA-256 hex.
- `EmptySha256` is the hash for an empty request body.

## Secrets

### `IMagicSecretProvider`

```csharp
ValueTask<MagicSecretLease<T>> GetAsync<T>(
    string name,
    CancellationToken cancellationToken = default);
```

Fetches a secret only when requested. The built-in provider requires an active control-plane connection and creates a fresh proof bound to the secret name and endpoint.

### `MagicSecretLease<T>`

| Property | Purpose |
|---|---|
| `Value` | Resolved typed secret value. |
| `ExpiresUtc` | Optional server-supplied expiration. |

Dispose the lease after use. Disposal cannot guarantee erasure of immutable managed strings; prefer byte-oriented custom secret APIs for extremely sensitive material.

### `IMagicSecretTransport`

Transport extension point for `MagicSecretRequest` / `MagicSecretResponse`.

---

# MagicSettings.Share package

## Remote configuration contracts

### `MagicRemoteValue`

Represents one remote path value.

- `From<T>` creates a serialized value.
- `ExplicitNull` explicitly overrides a lower provider with `null`.
- `Durability` controls sticky, refreshable, or expiring behavior.
- `ExpiresUtc` is meaningful for expiring values.

### `MagicRemoteSnapshot`

A complete node-specific remote layer. A newer snapshot replaces the previous remote layer wholesale. Omitted paths reveal lower-priority providers. `Empty` represents no remote overrides.

### `MagicValueState`

- `Value`
- `Null`

### `MagicRemoteValueDurability`

- `Sticky`: survives control-plane outages until replaced.
- `Refreshable`: ordinary refreshable remote value.
- `Expiring`: removed from the effective remote layer after `ExpiresUtc`.

## Schema and migration contracts

### `MagicSettingManifestEntry`

Describes path, CLR type, nullability, sensitivity, remote-override permission, and optional human description.

### `MagicSettingsSchemaManifest`

Describes application ID/version, schema version, deterministic schema fingerprint, and all discovered settings entries.

### `MagicSettingsMigrationReport`

Contains local safe operations and review items generated while migrating one node.

### `MagicMigrationReviewItem`

Contains affected path, operation, severity, reason, and optional proposed replacement path.

### `MagicMigrationReviewSeverity`

- `Information`
- `Warning`
- `Destructive`

## Control-plane contracts

### `MagicControlPlaneEndpointSource`

Identifies whether the selected endpoint came from no source, code fallback, persistent settings, environment variable, or runtime override.

### `MagicResolvedControlPlaneEndpoint`

Contains the endpoint and source. `None` represents disabled remote synchronization.

### `MagicControlPlaneState`

- `Disabled`
- `Configured`
- `Connecting`
- `PendingApproval`
- `Active`
- `Disconnected`
- `Faulted`

### `MagicControlPlaneTrust`

Use `SystemTls(authorityId)` for normal platform TLS validation or `Pinned(authorityId, fingerprint)` for public-key fingerprint pinning. `AuthorityId` is also the expected proof audience.

### `MagicSettingsSyncRequest`

Carries public identity, request proof, schema manifest, last remote revision, optional migration report, and optional rotation continuity proof.

### `MagicSettingsSyncResponse`

Carries state, complete remote snapshot, optional message, and optional suggested polling interval.

## Authentication contracts

### `MagicNodeIdentityDescriptor`

Safe public identity descriptor: node ID, credential ID, credential kind, signature algorithm, public key, fingerprint, and creation time.

### `MagicAuthenticationRequest`

Input to `CreateProofAsync`: audience, HTTP method, target URI, body hash, and optional proof lifetime.

### `MagicAuthenticationProof`

Signed proof containing version, node and credential IDs, audience, method, normalized target, body hash, issuance, expiration, nonce, and signature.

A proof is not a reusable API key. It is valid only for the exact request it signs and must be rejected after nonce reuse.

### `MagicIdentityContinuityProof`

Binds an old approved public credential to a new public credential using a signature from the old private key.

### `MagicIdentityChange`

Describes `Rotated`, `Reset`, or `RecoveredAfterLoss` changes and includes current/previous descriptors plus an optional continuity proof.

### `MagicIdentityResetRequest`

Contains a human reason and the required destructive confirmation flag.

### `MagicProofVerificationRequest`

Server-side verification context: proof, expected audience, actual method/URI/body hash, and current time.

### `MagicProofVerificationResult`

Use `IsValid` and `Error`; helper members `Valid` and `Invalid(error)` create results.

### `MagicNodeProofCodec`

Encodes/decodes proofs for the `MagicNode` authorization header and provides Base64URL helpers. Decoding does not verify authenticity; always pass decoded proofs through `MagicNodeProofVerifier`.

### `MagicSettingsSyncProof.ComputeBodySha256`

Computes the deterministic hash covered by a synchronization proof, including identity, schema metadata, revision, migration report, and optional continuity proof.

### `MagicSecretProof.ComputeBodySha256`

Computes the deterministic body hash for a named secret request.

---

# MagicSettings.Server package

## Proof verification

### `MagicNodeProofVerifier`

```csharp
ValueTask<MagicProofVerificationResult> VerifyAsync(MagicProofVerificationRequest request, CancellationToken cancellationToken = default);
ValueTask<MagicProofVerificationResult> VerifyEnrollmentAsync(MagicNodeIdentityDescriptor identity, MagicProofVerificationRequest request, CancellationToken cancellationToken = default);
static bool VerifyContinuity(MagicIdentityContinuityProof proof);
```

`VerifyAsync` requires a known credential in `Approved` or `Retiring` state. It validates proof version, audience, method, target, body hash, clock bounds, maximum lifetime, signature, and one-time nonce.

`VerifyEnrollmentAsync` verifies an unknown node using the public key supplied in its identity descriptor before a pending credential record is created.

`VerifyContinuity` validates that a new credential belongs to the same logical node and was authorized by the old credential.

### `MagicAspNetProofVerificationExtensions.VerifyHttpRequestAsync`

Reads an ASP.NET request, decodes the `MagicNode` header, buffers and restores the body stream, computes the actual body hash, reconstructs the request URI, and delegates to `MagicNodeProofVerifier`.

## Credential storage and lifecycle

### `MagicRegisteredCredential`

Stores node ID, credential ID, public key, status, and update time. It never contains a private key.

### `IMagicCredentialRegistry`

```csharp
ValueTask<MagicRegisteredCredential?> FindAsync(Guid nodeId, Guid credentialId, CancellationToken cancellationToken = default);
ValueTask UpsertAsync(MagicRegisteredCredential credential, CancellationToken cancellationToken = default);
```

Implement with durable server storage for production.

### `MagicCredentialAdministrationService`

- `SetStatusAsync` changes a known credential status.
- `ApproveAsync` marks a credential approved.
- `RevokeAsync` marks a credential revoked.

Returning `false` means the credential was not found.

### `MagicCredentialRotationService.ApplyAsync`

Validates the continuity proof, verifies the previous registered credential, marks it retiring, and stores the new credential as approved or pending according to policy.

### `MagicCredentialStatus`

- `Pending`
- `Approved`
- `Retiring`
- `Revoked`

### `InMemoryMagicCredentialRegistry`

Testing and single-process demonstration implementation. It is not durable and does not distribute authorization state to other API processes.

## Replay defense

### `IMagicReplayCache`

```csharp
ValueTask<bool> TryUseAsync(
    Guid credentialId,
    string nonce,
    DateTimeOffset expiresUtc,
    CancellationToken cancellationToken = default);
```

Returns `true` exactly once for a credential/nonce pair. Production implementations must be shared or otherwise consistent across all replicas that accept the same credential.

### `InMemoryMagicReplayCache`

Suitable for tests or one API process. It cannot prevent replay across multiple replicas.

## Synchronization storage

### `MagicNodeRemoteRecord`

Stores per-node and per-application schema metadata, remote revision/snapshot, pending migration review items, and last-contact time.

### `IMagicNodeRemoteRecordStore`

Loads and saves one node/application record. Production implementations should use durable storage and concurrency controls appropriate to their control plane.

### `InMemoryMagicNodeRemoteRecordStore`

Testing and demonstration implementation only.

### `MagicSettingsSyncService.SynchronizeAsync`

Validates the signed synchronization payload, handles enrollment and optional continuity proofs, returns pending/active/faulted state, stores schema metadata, retains destructive migration review items, and returns the node-specific snapshot.

The service does not automatically delete remote values merely because a client schema no longer consumes them.

## Secrets

### `IMagicSecretResolver`

```csharp
ValueTask<MagicSecretResponse> ResolveAsync(
    Guid nodeId,
    string name,
    CancellationToken cancellationToken = default);
```

Application-provided server extension point. It decides authorization, storage, auditing, and value retrieval after node proof verification.

### `MagicSecretService.ResolveAsync`

Checks that request identity fields match the proof, verifies the request against the expected authority audience, method, URI, and secret-name hash, then delegates to `IMagicSecretResolver`.

---

# Security rules for API consumers

1. Never log, return, serialize to telemetry, or transmit `MagicStoredNodeIdentity.PrivateKey`.
2. Never treat `MagicAuthenticationProof` as a reusable bearer token.
3. Use a distinct stable audience for each relying API or trust domain.
4. Verify the actual HTTP method, target URI, and body hash at the receiver.
5. Store replay nonces in a cache shared by every replica accepting the same credentials.
6. Reset does not revoke a stolen old credential; server-side revocation remains mandatory.
7. Do not permit a remote settings snapshot to choose its own control-plane endpoint.
8. Do not enable non-loopback HTTP merely to make deployment easier.
9. In-memory server stores are examples and test helpers, not production durability.
10. Treat destructive migration review items as administrative decisions, not automatic cleanup instructions.
