using System.Security.Cryptography;
using System.Text;

namespace MagicSettings.Share;

public static class MagicSecretProof
{
    public static string ComputeBodySha256(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
    }
}
