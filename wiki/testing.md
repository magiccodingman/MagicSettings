# Testing

The test suite covers endpoint bootstrap precedence, the local-only endpoint rule, generation and non-destructive reconciliation, strict development array policy, remote-over-environment-over-file precedence, non-persistence of remote values, stable identity, rotation continuity, destructive reset confirmation, audience/method/URI/body proof binding, replay rejection, self-signed enrollment, rotation through normal synchronization, revocation, asynchronous secret proof verification, endpoint hijack prevention, migrations, and retention of destructive review items.

Run:

```bash
dotnet restore MagicSettings.sln
dotnet build MagicSettings.sln --configuration Release --no-restore
dotnet test MagicSettings.sln --configuration Release --no-build
```
