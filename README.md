# MagicSettings

MagicSettings is a client-owned .NET configuration system for applications that need more than copied `appsettings.{Environment}.json` files.

It defines settings in code, keeps one persistent JSON document current, applies explicit migrations, composes transient overrides without writing secrets back to disk, supports live reloads, and optionally lets each application instance initiate a secure relationship with a control plane.

## What it provides

- Strongly typed code-defined templates.
- Automatic first-run `appsettings.json` generation.
- Non-destructive reconciliation that adds newly introduced properties.
- Explicit sequential migrations for renames, removals, and transformations.
- Conservative array handling with per-path merge policies.
- Runtime precedence: persistent file → custom providers → OS environment → remote in-memory overrides.
- Standard `IConfiguration`, `IOptions<T>`, and `IOptionsMonitor<T>` compatibility.
- A stable installation identity created during first initialization.
- Client-initiated control-plane synchronization with startup or later activation.
- Request-bound cryptographic proofs that can authenticate the same node to other APIs.
- Credential rotation, revocation helpers, and destructive identity reset without exposing private key material.
- Explicit asynchronous on-demand secret retrieval using the same node proof model.
- Storage-agnostic server helpers for enrollment, authorization caches, replay defense, and migration review.

## Quick start

```csharp
var builder = WebApplication.CreateBuilder(args);

var magic = await builder.AddMagicSettingsAsync<AppSettings>(
    args,
    options =>
    {
        options.ApplicationId = "MagicAiGateway";
        options.SchemaVersion = 3;
        options.Template = AppSettings.CreateDefaults();

        options.ArrayPolicies["AllowedOrigins"] =
            MagicArrayMergePolicy.Union;

        options.ControlPlane.Bootstrap.EnvironmentVariableName =
            "MAGICSETTINGS_CONTROL_PLANE_ENDPOINT";
        options.ControlPlane.Bootstrap.PersistentSettingPath =
            "MagicSettings:ControlPlane:Endpoint";
        options.ControlPlane.Bootstrap.Trust =
            MagicControlPlaneTrust.SystemTls("MagicSettings.ControlPlane");
        options.ControlPlane.Bootstrap.ConnectOnStartup = true;
    });

if (magic.ShouldExit)
{
    return;
}
```

Ordinary .NET configuration consumers continue to work:

```csharp
var host = builder.Configuration["Database:Host"];
builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection("Database"));
```

Advanced operations use explicit MagicSettings services:

```csharp
var controlPlane = app.Services
    .GetRequiredService<IMagicSettingsControlPlane>();

await controlPlane.ConfigureAsync(
    discoveredEndpoint,
    MagicControlPlaneTrust.Pinned(
        "MagicSettings.ControlPlane",
        expectedServerFingerprint));
```

## Configuration precedence

```text
remote in-memory snapshot       highest
MagicSettings__ OS environment
custom source providers
persistent appsettings.json     lowest runtime source
```

The code template is not a transient override. It generates and repairs the persistent document. Environment, custom-provider, and remote values are never serialized into that file.

## Control-plane endpoint bootstrap

A node may know its control plane during startup, discover it later, or never use one. Endpoint resolution is deliberately local-only:

```text
explicit ConfigureAsync(...)
dedicated OS environment variable
persistent local appsettings value
code fallback
no endpoint / remote disabled
```

A remote setting cannot redirect the node to a new authority. Authority transitions require application code or another trusted local bootstrap source.

## Node authentication

MagicSettings does **not** expose a normal `GetPrivateKey()` API. Application code asks `IMagicNodeAuthenticator` to create a fresh proof bound to:

- the intended API audience,
- HTTP method,
- exact target URI,
- request-body SHA-256,
- a short validity window,
- and a one-time nonce.

```csharp
var authenticator = services
    .GetRequiredService<IMagicNodeAuthenticator>();

var identity = await authenticator.GetCurrentIdentityAsync();
var proof = await authenticator.CreateProofAsync(
    new MagicAuthenticationRequest(
        "Inventory.Api",
        "POST",
        requestUri,
        MagicHash.Sha256Hex(body)));
```

For normal `HttpClient` usage:

```csharp
services.AddHttpClient<InventoryClient>()
    .AddMagicNodeAuthentication("Inventory.Api");
```

The identity descriptor contains only public information: node ID, credential ID, algorithm, public key, and fingerprint.

On-demand secrets remain explicit and asynchronous:

```csharp
var secrets = app.Services.GetRequiredService<IMagicSecretProvider>();
await using var password = await secrets.GetAsync<string>("Database:Password");
```

The default HTTP transport signs the secret name and request target. It never writes the returned value into `appsettings.json`.

## Identity lifecycle

- **Rotation** keeps the logical node ID, creates a new credential, and produces a continuity proof signed by the old credential.
- **Reset** creates a completely new node identity and requires server approval again.
- Losing the identity file has the same trust consequence as reset: the replacement identity is not the old authorized node.
- Local regeneration does not revoke a stolen credential. The authorization server must revoke the old credential and distribute that change to relying APIs.

## Documentation

- [Getting started](wiki/getting-started.md)
- [Architecture and ownership](wiki/configuration.md)
- [Configuration precedence](wiki/configuration.md)
- [Paths and environments](wiki/configuration.md)
- [Reconciliation and arrays](wiki/configuration.md)
- [Migrations](wiki/migrations.md)
- [Providers](wiki/configuration.md)
- [Control-plane integration](wiki/control-plane.md)
- [Endpoint bootstrap](wiki/control-plane.md)
- [Identity and enrollment](wiki/control-plane.md)
- [API authentication proofs](wiki/control-plane.md)
- [Rotation, reset, and revocation](wiki/control-plane.md)
- [Outages and stale state](wiki/control-plane.md)
- [Secrets](wiki/secrets-and-security.md)
- [Diagnostics](wiki/configuration.md)
- [Security boundaries](wiki/secrets-and-security.md)
- [Testing](wiki/testing.md)

## Security boundary

MagicSettings can authenticate a node, sign request-specific proofs, preserve provider provenance, keep remote values out of the persistent file, and help a server reject replayed requests. It cannot protect secrets from a process or administrator that already controls the machine, securely erase arbitrary managed strings, or revoke a stolen credential without server participation.

Licensed under Apache-2.0.
