using System.Security.Cryptography;
using MagicSettings.Share;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Text;

namespace MagicSettings.Server;

public sealed record MagicRegisteredCredential(
    Guid NodeId,
    Guid CredentialId,
    string PublicKey,
    MagicCredentialStatus Status,
    DateTimeOffset UpdatedUtc);
