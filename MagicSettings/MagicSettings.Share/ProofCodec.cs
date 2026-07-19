using System.Text;
using System.Text.Json;

namespace MagicSettings.Share;

public static class MagicNodeProofCodec
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Encode(MagicAuthenticationProof proof)
        => Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(proof, Json));

    public static MagicAuthenticationProof Decode(string encoded)
        => JsonSerializer.Deserialize<MagicAuthenticationProof>(Base64UrlDecode(encoded), Json)
           ?? throw new FormatException("The MagicSettings authentication proof is invalid.");

    public static string Base64UrlEncode(ReadOnlySpan<byte> value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
