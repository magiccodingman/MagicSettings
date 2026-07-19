using System.Security.Cryptography;
using MagicSettings.Share;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Text;

namespace MagicSettings.Server;

/// <summary>
/// Storage-agnostic credential lifecycle helpers for control-plane implementations.
/// </summary>
public sealed class MagicCredentialAdministrationService
{
    private readonly IMagicCredentialRegistry _credentials;

    public MagicCredentialAdministrationService(IMagicCredentialRegistry credentials) => _credentials = credentials;

    public async ValueTask<bool> SetStatusAsync(
        Guid nodeId,
        Guid credentialId,
        MagicCredentialStatus status,
        CancellationToken cancellationToken = default)
    {
        var credential = await _credentials.FindAsync(nodeId, credentialId, cancellationToken);
        if (credential is null)
        {
            return false;
        }

        await _credentials.UpsertAsync(credential with
        {
            Status = status,
            UpdatedUtc = DateTimeOffset.UtcNow
        }, cancellationToken);
        return true;
    }

    public ValueTask<bool> ApproveAsync(Guid nodeId, Guid credentialId, CancellationToken cancellationToken = default)
        => SetStatusAsync(nodeId, credentialId, MagicCredentialStatus.Approved, cancellationToken);

    public ValueTask<bool> RevokeAsync(Guid nodeId, Guid credentialId, CancellationToken cancellationToken = default)
        => SetStatusAsync(nodeId, credentialId, MagicCredentialStatus.Revoked, cancellationToken);
}
