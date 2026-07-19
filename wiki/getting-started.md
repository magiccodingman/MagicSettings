# Getting started

Call `AddMagicSettingsAsync<TSettings>` immediately after constructing the host builder. The call resolves the persistent path, creates or reconciles the JSON document, runs migrations, creates the installation identity, publishes the effective configuration into the standard .NET configuration chain, and registers background services.

```csharp
var builder = WebApplication.CreateBuilder(args);
var result = await builder.AddMagicSettingsAsync<AppSettings>(args, options =>
{
    options.ApplicationId = "Orders.Api";
    options.SchemaVersion = 1;
    options.Template = new AppSettings
    {
        Database = new() { Host = "localhost", Port = 5432 }
    };
});

if (result.ShouldExit)
{
    return;
}
```

The default persistent path is `AppContext.BaseDirectory/appsettings.json`. `MAGICSETTINGS_PATH` may point to either a JSON file or a directory.

## Command-line operations

- `--magic-settings-generate` reconciles the file and exits.
- `--magic-settings-force-generate` replaces the persistent document from the template and exits. A backup is retained by the atomic writer.
- `--magic-settings-validate` loads, binds, validates, and exits.
- `--magic-settings-print-path` prints the resolved file path and exits.

Destructive settings regeneration is separate from identity reset. Replacing `appsettings.json` does not change the node credential.
