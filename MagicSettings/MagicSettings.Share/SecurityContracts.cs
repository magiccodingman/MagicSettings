namespace MagicSettings.Share;

public enum MagicCredentialKind
{
    EcdsaP256
}

public enum MagicCredentialStatus
{
    Pending,
    Approved,
    Retiring,
    Revoked
}

public sealed record MagicNodeIdentityDescriptor(
    Guid NodeId,
    Guid CredentialId,
    MagicCredentialKind CredentialKind,
    string SignatureAlgorithm,
    string PublicKey,
    string Fingerprint,
    DateTimeOffset CreatedUtc);

public sealed record MagicAuthenticationRequest(
    string Audience,
    string Method,
    Uri Uri,
    string BodySha256,
    TimeSpan? ValidFor = null);

public sealed record MagicAuthenticationProof(
    string Version,
    Guid NodeId,
    Guid CredentialId,
    string Audience,
    string Method,
    string Target,
    string BodySha256,
    DateTimeOffset IssuedUtc,
    DateTimeOffset ExpiresUtc,
    string Nonce,
    string Signature);

public sealed record MagicIdentityContinuityProof(
    MagicNodeIdentityDescriptor PreviousIdentity,
    MagicNodeIdentityDescriptor NewIdentity,
    DateTimeOffset IssuedUtc,
    string Nonce,
    string Signature);

public enum MagicIdentityChangeKind
{
    Rotated,
    Reset,
    RecoveredAfterLoss
}

public sealed record MagicIdentityChange(
    MagicIdentityChangeKind Kind,
    MagicNodeIdentityDescriptor Current,
    MagicNodeIdentityDescriptor? Previous,
    MagicIdentityContinuityProof? ContinuityProof,
    string Reason);

public sealed record MagicIdentityResetRequest(string Reason, bool ConfirmDestructiveReset);

public sealed record MagicProofVerificationRequest(
    MagicAuthenticationProof Proof,
    string ExpectedAudience,
    string Method,
    Uri Uri,
    string BodySha256,
    DateTimeOffset NowUtc);

public sealed record MagicProofVerificationResult(bool IsValid, string? Error)
{
    public static MagicProofVerificationResult Valid { get; } = new(true, null);
    public static MagicProofVerificationResult Invalid(string error) => new(false, error);
}
