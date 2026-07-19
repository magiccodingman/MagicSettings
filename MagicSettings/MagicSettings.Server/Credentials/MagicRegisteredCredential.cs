namespace MagicSettings.Server;

public sealed record MagicRegisteredCredential(
    Guid NodeId,
    Guid CredentialId,
    string PublicKey,
    MagicCredentialStatus Status,
    DateTimeOffset UpdatedUtc);
