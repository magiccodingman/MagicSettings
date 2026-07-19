namespace MagicSettings;

public sealed class MagicNodeAuthenticationHandler : DelegatingHandler
{
    private readonly IMagicNodeAuthenticator _authenticator;
    private readonly string _audience;

    public MagicNodeAuthenticationHandler(IMagicNodeAuthenticator authenticator, string audience)
    {
        _authenticator = authenticator;
        _audience = audience;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.RequestUri);
        var body = request.Content is null
            ? Array.Empty<byte>()
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        if (request.Content is not null)
        {
            var replacement = new ByteArrayContent(body);
            foreach (var header in request.Content.Headers)
            {
                replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            request.Content = replacement;
        }

        var proof = await _authenticator.CreateProofAsync(
            new(
                _audience,
                request.Method.Method,
                request.RequestUri,
                MagicHash.Sha256Hex(body)),
            cancellationToken);

        request.Headers.Authorization = new AuthenticationHeaderValue("MagicNode", MagicNodeProofCodec.Encode(proof));
        request.Headers.TryAddWithoutValidation("X-Magic-Node-Id", proof.NodeId.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-Magic-Credential-Id", proof.CredentialId.ToString("D"));
        return await base.SendAsync(request, cancellationToken);
    }
}
