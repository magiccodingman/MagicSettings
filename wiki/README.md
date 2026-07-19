# MagicSettings documentation

MagicSettings is intentionally split into three packages:

- `MagicSettings` contains the application runtime, persistent document handling, endpoint bootstrap, identity storage, proof creation, and hosted synchronization.
- `MagicSettings.Share` contains contracts understood by both applications and servers.
- `MagicSettings.Server` contains storage-agnostic helpers. It is not a complete control-plane product.

## Guides

1. [API reference](api-reference.md)
2. [AI agent usage guide](agent-usage.md)
3. [Getting started](getting-started.md)
4. [Architecture](configuration.md)
5. [Configuration precedence](configuration.md)
6. [Paths and environments](configuration.md)
7. [Reconciliation and arrays](configuration.md)
8. [Migrations](migrations.md)
9. [Providers](configuration.md)
10. [Control-plane integration](control-plane.md)
11. [Secrets](secrets-and-security.md)
12. [Diagnostics](configuration.md)
13. [Security boundaries](secrets-and-security.md)
14. [Testing](testing.md)
