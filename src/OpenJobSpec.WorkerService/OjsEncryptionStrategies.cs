using System.Security.Cryptography;
using System.Text;

namespace OpenJobSpec.WorkerService;

internal sealed class OjsLocalAes256GcmCodec : IDisposable
{
    private const string EncryptedPrefix = "ojs-encrypted:";
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly AesGcm _aesGcm;

    internal OjsLocalAes256GcmCodec(string base64Key)
    {
        var keyBytes = Convert.FromBase64String(base64Key);
        _aesGcm = new AesGcm(keyBytes, TagSizeBytes);
    }

    internal string Encrypt(string plaintext)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];

        _aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var result = new byte[NonceSizeBytes + ciphertext.Length + TagSizeBytes];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSizeBytes, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSizeBytes + ciphertext.Length, TagSizeBytes);

        return EncryptedPrefix + Convert.ToBase64String(result);
    }

    internal string Decrypt(string encryptedValue)
    {
        if (!encryptedValue.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            throw new ArgumentException("Value is not encrypted (missing ojs-encrypted: prefix)");

        var data = Convert.FromBase64String(encryptedValue[EncryptedPrefix.Length..]);
        var nonce = data.AsSpan(0, NonceSizeBytes);
        var ciphertext = data.AsSpan(NonceSizeBytes, data.Length - NonceSizeBytes - TagSizeBytes);
        var tag = data.AsSpan(data.Length - TagSizeBytes);
        var plaintext = new byte[ciphertext.Length];

        _aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    internal static bool IsEncrypted(string value) =>
        value?.StartsWith(EncryptedPrefix, StringComparison.Ordinal) == true;

    public void Dispose()
    {
        _aesGcm.Dispose();
    }
}

internal sealed class OjsRemoteCodecClient : IDisposable
{
    private readonly HttpClient _client;

    internal OjsRemoteCodecClient(string baseUrl)
    {
        _client = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
