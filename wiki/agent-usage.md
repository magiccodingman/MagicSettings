# AI agent usage guide

This guide is written for coding agents and developers integrating MagicSettings into another repository. Use it as a task-to-API map. For exact symbol descriptions, see [API reference](api-reference.md).

## Read this first

MagicSettings has three different concepts that agents must not collapse into one:

1. **Persistent configuration**: the local JSON document maintained from the code template.
2. **Transient overrides**: custom providers, OS environment variables, and remote snapshots layered above the file.
3. **Node identity**: a separate cryptographic credential used for enrollment and request authentication.

Regenerating settings must not reset identity. Resetting identity must not rewrite settings. Remote values must not be persisted into the local JSON file.

## Task index

| Goal | Primary API |
|---|---|
| Add MagicSettings to an application | `AddMagicSettingsAsync<TSettings>` |
| Read typed effective settings | `IMagicSettings<TSettings>` or `IOptions<TSettings>` |
| Determine where a value came from | `IMagicSettings<TSettings>.Explain` |
| Add an environment-specific default | `MagicSettingsOptions<TSettings>.ConfigureEnvironment` |
| Add a new settings property | Update `TSettings.Template`; reconciliation adds it locally |
| Rename or transform a setting | `IMagicSettingsMigration` + `MagicMigrationContext` |
| Define array behavior | `ArrayPolicies[path] = MagicArrayMergePolicy...` |
| Add a custom bulk source | `IMagicSettingsSourceProvider` |
| Discover/connect to a control plane later | `IMagicSettingsControlPlane.ConfigureAsync` |
| Force immediate remote refresh | `IMagicSettingsControlPlane.RefreshAsync` |
| Authenticate to another API | `AddMagicNodeAuthentication` or `IMagicNodeAuthenticator.CreateProofAsync` |
| Inspect public node identity | `IMagicNodeAuthenticator.GetCurrentIdentityAsync` |
| Rotate a healthy credential | `IMagicNodeIdentityManager.RotateAsync` |
| Replace a lost/compromised identity | `IMagicNodeIdentityManager.ResetAsync` |
| Resolve a secret on demand | `IMagicSecretProvider.GetAsync<T>` |
| Verify a signed API request | `MagicNodeProofVerifier` or `VerifyHttpRequestAsync` |
| Approve/revoke a credential | `MagicCredentialAdministrationService` |
| Implement server persistence | `IMagicCredentialRegistry`, `IMagicReplayCache`, `IMagicNodeRemoteRecordStore` |

---

# Application integration workflows

## 1. Add MagicSettings to a new application

```csharp
var builder = WebApplication.CreateBuilder(args);

var initialization = await builder.AddMagicSettingsAsync<AppSettings>(
    args,
    options =>
    {
        options.ApplicationId = "Orders.Api";
        options.ApplicationVersion = "1.0.0";
        options.SchemaVersion = 1;
        options.Template = AppSettings.CreateDefaults();

        options.SensitivePaths.Add("Database:Password");
        options.ArrayPolicies["Cors:AllowedOrigins"] =
            MagicArrayMergePolicy.Union;
    });

if (initialization.ShouldExit)
{
    return;
}

var app = builder.Build();
```

Agent rules:

- Call it exactly once per host.
- Call it before `builder.Build()`.
- Do not separately register another MagicSettings runtime for the same `TSettings`.
- Do not write environment or remote values into `options.Template` at runtime.
- Treat `Template` as the persistent-file schema/default source.

## 2. Consume settings

Standard .NET consumption:

```csharp
builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection("Database"));
```

MagicSettings-specific consumption:

```csharp
public sealed class Worker(IMagicSettings<AppSettings> settings)
{
    public void Run()
    {
        var current = settings.Current;
        var revision = settings.Revision;
    }
}
```

Listen for validated changes:

```csharp
settings.Changed += (_, change) =>
{
    logger.LogInformation(
        "Settings revision {Revision} activated",
        change.Revision);
};
```

Do not mutate `settings.Current` and expect persistence or publication. It is a snapshot, not an editing API.

## 3. Explain precedence

```csharp
var explanation = settings.Explain("Database:Host");
```

Use this for diagnostics, support tooling, and tests. Sensitive paths are redacted.

Effective precedence, lowest to highest:

```text
persistent JSON
custom providers
MagicSettings__ environment variables
remote snapshot
```

## 4. Configure environment-specific defaults

```csharp
options.ConfigureEnvironment("Development", settings =>
{
    settings.Database.Host = "localhost";
    settings.Logging.MinimumLevel = "Debug";
});
```

This changes the code template before reconciliation. It does not create a hidden runtime provider.

## 5. Add a setting without breaking existing installations

1. Add the property to the typed settings model.
2. Add its default to the template.
3. Leave `SchemaVersion` unchanged when adding a straightforward missing property.
4. Reconciliation will add it to existing files while preserving existing values.

Increase `SchemaVersion` only when a semantic migration is required.

## 6. Migrate a renamed setting

```csharp
public sealed class DatabaseHostMigration : IMagicSettingsMigration
{
    public int FromVersion => 1;
    public int ToVersion => 2;

    public void Apply(JsonObject document, MagicMigrationContext context)
    {
        context.Rename(
            document,
            "Database:Server",
            "Database:Host");
    }
}
```

Register it:

```csharp
options.SchemaVersion = 2;
options.Migrations.Add(new DatabaseHostMigration());
```

Agent rules:

- Every schema version transition must be reachable through sequential migrations.
- Never silently skip a version.
- Use `Remove` for local deletion; it deliberately creates a destructive server review item.
- Mark a transformation remotely safe only when the same transformation is unquestionably valid for stored server values.

## 7. Configure arrays

```csharp
options.ArrayPolicies["Cors:AllowedOrigins"] =
    MagicArrayMergePolicy.Union;
```

Choose policy from the actual semantics:

- Ordered middleware/pipeline: usually `PreserveExisting` or `ReplaceWithTemplate`.
- Set-like origins or capabilities: often `Union`.
- Operator-curated lists: usually `PreserveExisting`.

Never select `Union` merely to suppress a strict-development error.

## 8. Add a custom provider

```csharp
public sealed class LocalDatabaseSettingsProvider : IMagicSettingsSourceProvider
{
    public string Name => "LocalDatabase";
    public int Priority => 100;

    public async ValueTask<IReadOnlyDictionary<string, string?>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        return await LoadCompleteSnapshotAsync(cancellationToken);
    }
}
```

```csharp
options.Providers.Add(new LocalDatabaseSettingsProvider());
```

Return a complete snapshot for that provider, not only changes since the previous call.

---

# Control-plane workflows

## 9. Configure startup endpoint bootstrap

```csharp
options.ControlPlane.Bootstrap.EnvironmentVariableName =
    "MAGICSETTINGS_CONTROL_PLANE_ENDPOINT";

options.ControlPlane.Bootstrap.PersistentSettingPath =
    "MagicSettings:ControlPlane:Endpoint";

options.ControlPlane.Bootstrap.CodeFallbackEndpoint =
    new Uri("https://control.internal/");

options.ControlPlane.Bootstrap.Trust =
    MagicControlPlaneTrust.SystemTls("MagicSettings.ControlPlane");

options.ControlPlane.Bootstrap.ConnectOnStartup = true;
```

Bootstrap resolution is local-only. Do not change code to resolve the endpoint from `builder.Configuration` after MagicSettings providers are composed, because that effective configuration may contain remote values.

## 10. Connect after service discovery

```csharp
var controlPlane = services
    .GetRequiredService<IMagicSettingsControlPlane>();

await controlPlane.ConfigureAsync(
    discoveredEndpoint,
    MagicControlPlaneTrust.Pinned(
        "MagicSettings.ControlPlane",
        expectedPublicKeyFingerprint),
    cancellationToken);
```

Use this when discovery occurs after startup or another local service tells the application where its control plane lives.

The new endpoint does not become authoritative until synchronization succeeds.

## 11. Disconnect

Keep the last known good remote snapshot:

```csharp
await controlPlane.DisconnectAsync(
    clearRemoteOverrides: false,
    cancellationToken);
```

Reveal lower-priority providers immediately:

```csharp
await controlPlane.DisconnectAsync(
    clearRemoteOverrides: true,
    cancellationToken);
```

Choose intentionally; clearing remote values may alter live application behavior.

## 12. Implement a custom transport

Implement `IMagicControlPlaneTransport`. Implement `IMagicSecretTransport` as well when the same transport should support `IMagicSecretProvider` registration.

The custom transport must:

- Send the exact signed synchronization request.
- Validate the supplied trust policy.
- Avoid redirects that change the signed target or authority.
- Preserve cancellation and timeout behavior.
- Return a complete snapshot, not a partial patch.

---

# Node authentication workflows

## 13. Authenticate a normal `HttpClient`

```csharp
builder.Services
    .AddHttpClient<InventoryClient>()
    .AddMagicNodeAuthentication("Inventory.Api");
```

This is the preferred path. The handler signs each actual request and body.

The audience is not a display label. It is a security boundary and must match the receiving API exactly.

## 14. Create a proof for a custom protocol

```csharp
var authenticator = services
    .GetRequiredService<IMagicNodeAuthenticator>();

var proof = await authenticator.CreateProofAsync(
    new MagicAuthenticationRequest(
        Audience: "Inventory.Api",
        Method: "POST",
        Uri: requestUri,
        BodySha256: MagicHash.Sha256Hex(payload),
        ValidFor: TimeSpan.FromSeconds(60)),
    cancellationToken);
```

Transmit the proof and public node/credential IDs according to the custom protocol. Never transmit the private key.

Do not pre-create a proof and reuse it across calls. Each request needs a fresh nonce and signature.

## 15. Inspect public identity

```csharp
var identity = await authenticator.GetCurrentIdentityAsync(cancellationToken);
```

Safe fields include node ID, credential ID, algorithm, public key, fingerprint, and creation time.

The public key is not the secret. The private key remains inside the identity store/signing service.

## 16. Rotate a healthy credential

```csharp
var identityManager = services
    .GetRequiredService<IMagicNodeIdentityManager>();

var change = await identityManager.RotateAsync(
    "Scheduled annual rotation",
    cancellationToken);
```

Rotation:

- Keeps the node ID.
- Generates a new credential ID and keypair.
- Produces a continuity proof signed by the old credential.
- Sends that proof on the next synchronization.
- May be auto-approved or manually approved according to server policy.

Use only while the old key is still trusted.

## 17. Reset a lost or compromised identity

```csharp
var change = await identityManager.ResetAsync(
    new MagicIdentityResetRequest(
        "Credential may have been copied",
        ConfirmDestructiveReset: true),
    cancellationToken);
```

Reset:

- Generates a new node ID and credential.
- Has no continuity proof.
- Clears the current remote trust relationship.
- Requires approval as a new node.

After suspected compromise, also revoke the old server credential. Local reset alone does not neutralize a stolen key.

## 18. Provide a secure identity store

Implement `IMagicNodeIdentityStore` when plain file storage is not sufficient.

Requirements:

- Atomic replacement or transactional save.
- Access restricted to the application identity.
- No logging or telemetry of private material.
- Stable retrieval across restarts.
- Explicit deletion behavior.
- Prefer non-exportable hardware/OS-backed keys where possible.

`MagicStoredNodeIdentity` contains private material. Treat any code touching it as security-critical.

---

# Secret workflows

## 19. Resolve a secret

```csharp
var secretProvider = services
    .GetRequiredService<IMagicSecretProvider>();

await using var password = await secretProvider.GetAsync<string>(
    "Database:Password",
    cancellationToken);

UsePassword(password.Value);
```

The built-in provider requires an active control-plane connection. It signs the secret name and request target and does not write the result into the settings file.

Do not copy secret values into ordinary logs, exceptions, settings objects, or long-lived singleton state.

---

# Server implementation workflows

## 20. Verify an ASP.NET API request

```csharp
var result = await verifier.VerifyHttpRequestAsync(
    httpContext.Request,
    expectedAudience: "Inventory.Api",
    cancellationToken);

if (!result.IsValid)
{
    return Results.Unauthorized();
}
```

The API needs:

- `IMagicCredentialRegistry` populated with approved public credentials.
- `IMagicReplayCache` shared consistently across replicas.
- A stable expected audience.

## 21. Approve or revoke a node credential

```csharp
await administration.ApproveAsync(nodeId, credentialId, cancellationToken);
await administration.RevokeAsync(nodeId, credentialId, cancellationToken);
```

Approval and revocation should generate audit records in the hosting application.

APIs caching credential status must have a defined refresh/freshness policy. Revocation is only as fast as its distribution.

## 22. Implement production stores

Implement:

- `IMagicCredentialRegistry`
- `IMagicReplayCache`
- `IMagicNodeRemoteRecordStore`
- `IMagicSecretResolver` when secrets are enabled

The included in-memory classes are for tests, demos, and single-process experiments. They are not durable and do not coordinate replicas.

## 23. Process synchronization

Use `MagicSettingsSyncService.SynchronizeAsync` from the control-plane endpoint.

It handles:

- Signed payload validation.
- First-time proof-of-possession enrollment.
- Pending approval state.
- Approved credential validation.
- Rotation continuity proofs.
- Schema manifest retention.
- Migration review-item retention.
- Node-specific remote snapshot response.

Do not replace the service with logic that trusts node ID or hostname without a valid proof.

## 24. Resolve server-side secrets

Implement `IMagicSecretResolver`, then call `MagicSecretService.ResolveAsync` from the API endpoint.

The resolver receives the already verified node ID and requested name. The hosting application remains responsible for per-node authorization, audit, secret storage, and response policy.

---

# Agent anti-patterns

An AI agent modifying this library or integrating it elsewhere must not:

- Add `GetPrivateKey`, `ExportPrivateKey`, or a general certificate-with-private-key getter.
- Use one static signature as an API key.
- Disable replay protection because proofs are short-lived.
- Resolve the control-plane endpoint from the remote-effective configuration layer.
- Write remote or environment overrides back into `appsettings.json`.
- Reset identity as part of forced settings regeneration.
- Auto-delete server-side values because a client migration removed a path.
- Auto-approve a reset identity based only on matching application name, hostname, or node label.
- Use in-memory credential or replay stores in a replicated production API.
- Log complete proof payloads, secret values, or private identity records.
- Swallow migration-chain failures and continue with an unknown schema.
- Treat arrays as sets without explicit semantic justification.

# Recommended context order for agents

When an agent is asked to change MagicSettings behavior, read files in this order:

1. `README.md`
2. `wiki/agent-usage.md`
3. `wiki/api-reference.md`
4. The topic-specific wiki page.
5. Public contracts in `MagicSettings.Share`.
6. Public interfaces/options in `MagicSettings`.
7. Server helpers when the change affects enrollment, verification, or storage.
8. Tests covering the relevant behavior.

Then add or update tests before changing security-sensitive behavior.
