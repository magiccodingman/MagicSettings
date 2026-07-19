using System.Security.Cryptography;
using MagicSettings.Share;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Text;

namespace MagicSettings.Server;

public static class MagicAspNetProofVerificationExtensions
{
    public static async ValueTask<MagicProofVerificationResult> VerifyHttpRequestAsync(
        this MagicNodeProofVerifier verifier,
        HttpRequest request,
        string expectedAudience,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAudience);

        if (!request.Headers.TryGetValue("Authorization", out var authorization))
        {
            return MagicProofVerificationResult.Invalid("The MagicNode authorization header is missing.");
        }

        var header = authorization.ToString();
        const string prefix = "MagicNode ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return MagicProofVerificationResult.Invalid("The authorization scheme is not MagicNode.");
        }

        MagicAuthenticationProof proof;
        try
        {
            proof = MagicNodeProofCodec.Decode(header[prefix.Length..].Trim());
        }
        catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException)
        {
            return MagicProofVerificationResult.Invalid("The MagicNode authorization proof is malformed.");
        }

        request.EnableBuffering();
        string bodyHash;
        if (request.ContentLength is null or 0)
        {
            bodyHash = Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant();
        }
        else
        {
            await using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, cancellationToken);
            request.Body.Position = 0;
            bodyHash = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
        }

        var uri = new Uri($"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}");
        return await verifier.VerifyAsync(
            new(proof, expectedAudience, request.Method, uri, bodyHash, DateTimeOffset.UtcNow),
            cancellationToken);
    }
}
