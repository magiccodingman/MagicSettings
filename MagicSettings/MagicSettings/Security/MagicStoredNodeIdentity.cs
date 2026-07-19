namespace MagicSettings;

public sealed record MagicStoredNodeIdentity(
    Guid NodeId,
    Guid CredentialId,
    string PublicKey,
    string PrivateKey,
    DateTimeOffset CreatedUtc);
