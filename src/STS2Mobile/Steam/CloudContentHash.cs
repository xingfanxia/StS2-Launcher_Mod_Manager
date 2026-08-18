using System;
using System.Security.Cryptography;
using System.Text;

namespace STS2Mobile.Steam;

// Steam's EnumerateUserFiles manifest exposes the SHA-1 of each raw file.
// Matching it locally provides file-level delta sync without a download or an
// upload-begin RPC. Unknown formats deliberately fail open to a normal transfer.
internal static class CloudContentHash
{
    public static bool Matches(string remoteSha1, string localContent)
    {
        if (!TryDecode(remoteSha1, out var expected))
            return false;

        var actual = SHA1.HashData(Encoding.UTF8.GetBytes(localContent ?? ""));
        return actual.AsSpan().SequenceEqual(expected.AsSpan());
    }

    public static string DescribeFormat(string remoteSha1)
    {
        if (string.IsNullOrWhiteSpace(remoteSha1))
            return "missing";

        var encoded = remoteSha1.Trim();
        bool prefixed = encoded.StartsWith("sha1:", StringComparison.OrdinalIgnoreCase);
        if (prefixed)
            encoded = encoded[5..];
        if (encoded.Length == 40)
        {
            try
            {
                return Convert.FromHexString(encoded).Length == 20
                    ? (prefixed ? "sha1-hex40" : "hex40")
                    : "invalid-hex40";
            }
            catch (FormatException)
            {
                return "invalid-hex40";
            }
        }

        try
        {
            return Convert.FromBase64String(encoded).Length == 20
                ? "base64-20"
                : $"other-length-{encoded.Length}";
        }
        catch (FormatException)
        {
            return $"other-length-{encoded.Length}";
        }
    }

    private static bool TryDecode(string remoteSha1, out byte[] hash)
    {
        hash = null;
        if (string.IsNullOrWhiteSpace(remoteSha1))
            return false;

        var encoded = remoteSha1.Trim();
        if (encoded.StartsWith("sha1:", StringComparison.OrdinalIgnoreCase))
            encoded = encoded[5..];

        if (encoded.Length == 40)
        {
            try
            {
                hash = Convert.FromHexString(encoded);
                return hash.Length == 20;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        try
        {
            hash = Convert.FromBase64String(encoded);
            return hash.Length == 20;
        }
        catch (FormatException)
        {
            hash = null;
            return false;
        }
    }
}
