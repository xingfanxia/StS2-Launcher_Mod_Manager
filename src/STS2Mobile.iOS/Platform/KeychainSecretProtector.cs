using System;
using System.Security.Cryptography;
using System.Text;

namespace STS2Mobile.Platform;

// AES-256-GCM with a random key held in the Keychain (this device only), the
// counterpart of the Android Keystore-backed implementation. Format:
// "v1:" + base64(nonce[12] | tag[16] | ciphertext).
internal sealed class KeychainSecretProtector : ISecretProtector
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public bool Available => Key() != null;

    public string Encrypt(string plainText)
    {
        var key = Key();
        if (key == null || plainText == null)
            return null;
        var plain = Encoding.UTF8.GetBytes(plainText);
        var output = new byte[NonceSize + TagSize + plain.Length];
        RandomNumberGenerator.Fill(output.AsSpan(0, NonceSize));
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(
            output.AsSpan(0, NonceSize),
            plain,
            output.AsSpan(NonceSize + TagSize),
            output.AsSpan(NonceSize, TagSize)
        );
        return Prefix + Convert.ToBase64String(output);
    }

    public string Decrypt(string cipherText)
    {
        try
        {
            var key = Key();
            if (key == null || cipherText == null || !cipherText.StartsWith(Prefix))
                return null;
            var input = Convert.FromBase64String(cipherText.Substring(Prefix.Length));
            if (input.Length < NonceSize + TagSize)
                return null;
            var plain = new byte[input.Length - NonceSize - TagSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                input.AsSpan(0, NonceSize),
                input.AsSpan(NonceSize + TagSize),
                input.AsSpan(NonceSize, TagSize),
                plain
            );
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            PatchHelper.Log($"[Secrets] Decryption failed: {ex.GetType().Name}");
            return null;
        }
    }

    private static byte[] Key()
    {
        var key = IosNative.VaultKey(out var status);
        if (key == null)
            PatchHelper.Log($"[Secrets] Keychain key unavailable (OSStatus {status})");
        return key;
    }
}
