using System.Security.Cryptography;
using MagicSettings.Share;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Text;

namespace MagicSettings.Server;

public interface IMagicCredentialRegistry
{
    ValueTask<MagicRegisteredCredential?> FindAsync(Guid nodeId, Guid credentialId, CancellationToken cancellationToken = default);
    ValueTask UpsertAsync(MagicRegisteredCredential credential, CancellationToken cancellationToken = default);
}
