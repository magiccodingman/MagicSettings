namespace MagicSettings;

public sealed class FileMagicNodeIdentityStore : IMagicNodeIdentityStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public FileMagicNodeIdentityStore(string path) => _path = Path.GetFullPath(path);

    public async ValueTask<MagicStoredNodeIdentity?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<MagicStoredNodeIdentity>(stream, _json, cancellationToken);
    }

    public async ValueTask SaveAsync(MagicStoredNodeIdentity identity, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? AppContext.BaseDirectory);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, identity, _json, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        TryRestrictPermissions(temporary);
        File.Move(temporary, _path, overwrite: true);
        TryRestrictPermissions(_path);
    }

    public ValueTask DeleteAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        return ValueTask.CompletedTask;
    }

    private static void TryRestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
