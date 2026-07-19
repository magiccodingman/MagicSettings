# MagicSettings documentation

MagicSettings is intentionally split into three packages:

- `MagicSettings` contains the application runtime, persistent document handling, endpoint bootstrap, identity storage, proof creation, and hosted synchronization.
- `MagicSettings.Share` contains contracts understood by both applications and servers.
- `MagicSettings.Server` contains storage-agnostic helpers. It is not a complete control-plane product.

## Guides

1. [API reference](api-reference.md)
2. [AI agent usage guide](agent-usage.md)
3. [Source layout](source-layout.md)
4. [Getting started](getting-started.md)
5. [Architecture](configuration.md)
6. [Configuration precedence](configuration.md)
7. [Paths and environments](configuration.md)
8. [Reconciliation and arrays](configuration.md)
9. [Migrations](migrations.md)
10. [Providers](configuration.md)
11. [Control-plane integration](control-plane.md)
12. [Secrets](secrets-and-security.md)
13. [Diagnostics](configuration.md)
14. [Security boundaries](secrets-and-security.md)
15. [Testing](testing.md)
