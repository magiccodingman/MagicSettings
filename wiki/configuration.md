# Configuration

## Architecture and ownership

Every application instance owns its code-defined schema and defaults, persistent settings file, migration chain, installation identity, and effective runtime snapshot.

Remote communication is always initiated by the application. The server does not need a callback address and never discovers or pushes directly to a node. Two nodes running the same application can use different schema versions and settings. Updating Node A does not migrate Node B. A control-plane implementation may add groups or policies, but those are features above the MagicSettings protocol rather than assumptions inside the client library.

The server receives a schema manifest during synchronization. It may retain node-specific overrides and migration review items. Client-side removal means “this client no longer consumes this path,” not “destroy the server-side value.” Destructive server storage changes remain an explicit administrative concern.

## Configuration precedence

The runtime composes snapshots in this order:

1. Persistent JSON document.
2. Custom source providers, ordered by provider priority.
3. OS/process environment values using the `MagicSettings__` prefix.
4. Remote in-memory snapshot.

The last source wins. A complete incoming remote snapshot replaces the previous remote layer; paths absent from the new snapshot expose the next lower source again. The code template generates and reconciles the persistent file. It is not reapplied as a hidden high-priority runtime source.

`IMagicSettings<T>.Explain(path)` reports which sources contained a path and which one won. Paths registered in `SensitivePaths` are redacted.

## Paths and environments

Settings path resolution:

1. `MagicSettingsOptions.Path`.
2. `MAGICSETTINGS_PATH`.
3. `AppContext.BaseDirectory/appsettings.json`.

A path ending in `.json` is treated as a complete file path. Any other path is treated as a directory and the configured file name is appended. `AppContext.BaseDirectory` is used instead of the working directory because service managers, tests, containers, and desktop launchers commonly choose different working directories.

Environment resolution:

1. Explicit option.
2. `MAGICSETTINGS_ENVIRONMENT`.
3. `DOTNET_ENVIRONMENT`.
4. `ASPNETCORE_ENVIRONMENT`.
5. `Production`.

Development, Local, and Test use strict array-reconciliation defaults. Production preserves existing ambiguous arrays by default rather than turning a rollout mistake into a fleet-wide startup outage.

`MagicSettings__Database__Password` maps to `Database:Password`. Environment overrides are transient and never written to the persistent file. External changes to a running process's environment normally require restart; use a watched file, runtime configuration call, or remote provider for live updates.

The identity file is adjacent to the settings file by default. Set `options.IdentityPath` or `MAGICSETTINGS_IDENTITY_PATH` to place it elsewhere. A directory appends `IdentityFileName`; a file path is used directly. Identity placement is independent from settings regeneration.

## Reconciliation and arrays

Reconciliation adds missing object properties from the current template and preserves existing values. Unknown properties are preserved by default.

Arrays are not merged automatically because they may represent ordered pipelines, sets, operator-curated lists, or keyed objects. Configure the policy for every path whose template and persistent values may differ:

```csharp
options.ArrayPolicies["AllowedOrigins"] = MagicArrayMergePolicy.Union;
```

Policies are `PreserveExisting`, `ReplaceWithTemplate`, `AppendMissing`, and `Union`. In Development, Local, and Test, an ambiguous array throws during startup. In Production, the existing array is preserved by default.

Writes use a temporary file, backup, and atomic replacement. Runtime parse or validation failures retain the last known good snapshot.

## Providers and diagnostics

Implement `IMagicSettingsSourceProvider` for persistent or bulk external sources such as mounted files, a local database, or a vault cache. Providers return a complete path/value snapshot and are applied by priority.

On-demand secrets are separate. `IMagicSecretProvider.GetAsync<T>` is asynchronous and explicit because synchronous `IConfiguration["Secret"]` access cannot safely perform arbitrary network I/O.

Standard `IConfiguration` sees the already resolved effective snapshot. It cannot reveal every later semantic use after an application binds or copies a value; use `IMagicSettings<T>.Explain` for source provenance at resolution time.

Useful operational signals include current settings revision, path and environment, last successful reload, rejected candidate errors, control-plane state and endpoint source, last remote revision, node ID and public credential fingerprint, and pending or revoked credential state. Never log private keys, secret values, complete proofs, or raw enrollment tokens.
