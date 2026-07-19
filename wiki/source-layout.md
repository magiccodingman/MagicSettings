# Source layout

MagicSettings uses one focused top-level type per source file. File names match the primary type they contain, and feature folders describe the responsibility of that type. Avoid reintroducing numbered catch-all files such as `Source.01.cs`.

## `MagicSettings`

| Folder | Responsibility |
|---|---|
| `Abstractions` | Public interfaces and extension contracts consumed or implemented by applications. |
| `Configuration` | Persistent document handling, path/environment resolution, configuration providers, merge policies, and options. |
| `ControlPlane` | Endpoint resolution, synchronization orchestration, schema manifests, trust, and HTTP transport. |
| `Diagnostics` | Public value-provenance and explanation models. |
| `Metadata` | Attributes used to describe settings and remote-override behavior. |
| `Migrations` | Migration context and sequential migration execution. |
| `Runtime` | Host initialization, hosted reload/polling services, runtime snapshots, and command-line behavior. |
| `Security` | Node identity storage, signing, request authentication, hashing, rotation, and HTTP authentication helpers. |
| `Secrets` | Explicit asynchronous secret retrieval and secret leases. |

Project-wide imports live in `GlobalUsings.cs`; individual files should not carry a copied wall of unrelated imports.

## `MagicSettings.Share`

Shared protocol contracts remain grouped by protocol concern rather than split into one file per tiny record. These files define configuration, control-plane, proof, secret, security, and synchronization wire contracts used by both client and server packages.

## `MagicSettings.Server`

| Folder | Responsibility |
|---|---|
| `Abstractions` | Persistence and secret-resolution interfaces supplied by a control-plane host. |
| `Authentication` | HTTP proof extraction, signature verification, request binding, and replay defense. |
| `Credentials` | Approval, revocation, registration, and rotation lifecycle services. |
| `InMemory` | Test/development implementations of registries, record stores, and replay caches. |
| `Secrets` | Authenticated server-side secret resolution. |
| `Synchronization` | Node synchronization requests, remote records, and migration-review retention. |

## `MagicSettings.Tests`

Tests are grouped by the behavior they verify:

- `Configuration` for generation, precedence, endpoint bootstrap, arrays, and migrations.
- `Security` for enrollment, identity, credentials, proofs, rotation, revocation, and secrets.
- `Server` for server-side retention and safety behavior.
- `Infrastructure` for test-only settings models and temporary-directory helpers.

## Placement rules

When adding code:

1. Create a file named after its primary top-level type.
2. Place it in the narrowest existing feature folder.
3. Add a new feature folder only when the responsibility is genuinely distinct.
4. Keep protocol DTOs in `MagicSettings.Share` when both client and server must understand them.
5. Keep storage implementations out of the core runtime; depend on abstractions instead.
6. Keep tests beside the feature category they validate, not in numbered aggregate files.
7. Preserve public namespaces when moving files. Folder structure is for navigation and does not require namespace churn.
