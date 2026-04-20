using System.Security.Cryptography;
using System.Text;

namespace OpenJobSpec.AspNetCore;

internal static class OjsAes256GcmPayloadCodec
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    internal static string Decrypt(string base64Payload, string base64Key)
    {
        var payload = Convert.FromBase64String(base64Payload);
        var key = Convert.FromBase64String(base64Key);

        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("Encrypted payload is too short");

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(payload.Length - TagSize);
        var ciphertext = payload.AsSpan(NonceSize, payload.Length - NonceSize - TagSize);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    internal static string Encrypt(string plaintext, string base64Key)
    {
        var key = Convert.FromBase64String(base64Key);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSize];
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var payload = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(payload, 0);
        ciphertext.CopyTo(payload, NonceSize);
        tag.CopyTo(payload, NonceSize + ciphertext.Length);

        return Convert.ToBase64String(payload);
    }
}
