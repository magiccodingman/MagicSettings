namespace MagicSettings;

public sealed class MagicNodeIdentityManager : IMagicNodeIdentityManager, IMagicNodeAuthenticator
{
    private readonly IMagicNodeIdentityStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MagicStoredNodeIdentity? _current;

    public MagicNodeIdentityManager(IMagicNodeIdentityStore store) => _store = store;

    public event EventHandler<MagicIdentityChange>? IdentityChanged;

    public async ValueTask<MagicNodeIdentityDescriptor> GetCurrentAsync(CancellationToken cancellationToken = default)
        => MagicIdentityCryptography.Describe(await GetStoredAsync(cancellationToken));

    public ValueTask<MagicNodeIdentityDescriptor> GetCurrentIdentityAsync(CancellationToken cancellationToken = default)
        => GetCurrentAsync(cancellationToken);

    public async ValueTask<MagicAuthenticationProof> CreateProofAsync(MagicAuthenticationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Method);
        ArgumentNullException.ThrowIfNull(request.Uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BodySha256);

        var identity = await GetStoredAsync(cancellationToken);
        var issued = DateTimeOffset.UtcNow;
        var validFor = request.ValidFor ?? TimeSpan.FromMinutes(1);
        if (validFor <= TimeSpan.Zero || validFor > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Authentication proofs must be valid for more than zero and no longer than five minutes.");
        }

        var unsigned = new MagicAuthenticationProof(
            MagicIdentityCryptography.ProofVersion,
            identity.NodeId,
            identity.CredentialId,
            request.Audience,
            request.Method.ToUpperInvariant(),
            MagicIdentityCryptography.NormalizeTarget(request.Uri),
            request.BodySha256.ToLowerInvariant(),
            issued,
            issued.Add(validFor),
            MagicNodeProofCodec.Base64UrlEncode(RandomNumberGenerator.GetBytes(24)),
            string.Empty);

        return unsigned with { Signature = MagicIdentityCryptography.Sign(identity, MagicIdentityCryptography.Canonicalize(unsigned)) };
    }

    public async ValueTask<MagicIdentityChange> RotateAsync(string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var previous = await LoadOrCreateUnderLockAsync(cancellationToken);
            var next = MagicIdentityCryptography.Create(previous.NodeId);
            var previousDescriptor = MagicIdentityCryptography.Describe(previous);
            var nextDescriptor = MagicIdentityCryptography.Describe(next);
            var issued = DateTimeOffset.UtcNow;
            var nonce = MagicNodeProofCodec.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
            var canonical = MagicIdentityCryptography.CanonicalizeContinuity(previousDescriptor, nextDescriptor, issued, nonce);
            var continuity = new MagicIdentityContinuityProof(previousDescriptor, nextDescriptor, issued, nonce, MagicIdentityCryptography.Sign(previous, canonical));
            await _store.SaveAsync(next, cancellationToken);
            _current = next;
            var change = new MagicIdentityChange(MagicIdentityChangeKind.Rotated, nextDescriptor, previousDescriptor, continuity, reason);
            IdentityChanged?.Invoke(this, change);
            return change;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<MagicIdentityChange> ResetAsync(MagicIdentityResetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConfirmDestructiveReset)
        {
            throw new InvalidOperationException("Identity reset is destructive and requires ConfirmDestructiveReset=true.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var previous = await _store.LoadAsync(cancellationToken);
            var next = MagicIdentityCryptography.Create();
            await _store.SaveAsync(next, cancellationToken);
            _current = next;
            var change = new MagicIdentityChange(
                MagicIdentityChangeKind.Reset,
                MagicIdentityCryptography.Describe(next),
                previous is null ? null : MagicIdentityCryptography.Describe(previous),
                null,
                request.Reason);
            IdentityChanged?.Invoke(this, change);
            return change;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<MagicStoredNodeIdentity> GetStoredAsync(CancellationToken cancellationToken)
    {
        if (_current is not null)
        {
            return _current;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadOrCreateUnderLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<MagicStoredNodeIdentity> LoadOrCreateUnderLockAsync(CancellationToken cancellationToken)
    {
        if (_current is not null)
        {
            return _current;
        }

        _current = await _store.LoadAsync(cancellationToken);
        if (_current is null)
        {
            _current = MagicIdentityCryptography.Create();
            await _store.SaveAsync(_current, cancellationToken);
        }

        return _current;
    }
}
