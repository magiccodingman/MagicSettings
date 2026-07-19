# MagicSettings documentation

MagicSettings is intentionally split into three packages:

- `MagicSettings` contains the application runtime, persistent document handling, endpoint bootstrap, identity storage, proof creation, and hosted synchronization.
- `MagicSettings.Share` contains contracts understood by both applications and servers.
- `MagicSettings.Server` contains storage-agnostic helpers. It is not a complete control-plane product.

## Guides

1. [Getting started](getting-started.md)
2. [Architecture](configuration.md)
3. [Configuration precedence](configuration.md)
4. [Paths and environments](configuration.md)
5. [Reconciliation and arrays](configuration.md)
6. [Migrations](migrations.md)
7. [Providers](configuration.md)
8. [Control-plane integration](control-plane.md)
9. [Secrets](secrets-and-security.md)
10. [Diagnostics](configuration.md)
11. [Security boundaries](secrets-and-security.md)
12. [Testing](testing.md)
