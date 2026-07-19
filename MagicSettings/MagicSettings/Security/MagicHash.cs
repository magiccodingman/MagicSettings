namespace MagicSettings;

public static class MagicHash
{
    public static string Sha256Hex(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    public static string EmptySha256 { get; } = Sha256Hex(Array.Empty<byte>());
}
