using System.Security.Cryptography;
using System.Text;

namespace OpenJobSpec.WorkerService;

/// <summary>
/// Service that handles encryption/decryption of job arguments.
/// Uses AES-256-GCM for local encryption or can delegate to an OJS codec server.
/// </summary>
public sealed class OjsEncryptionService : IDisposable
{
    private const string EncryptedPrefix = "ojs-encrypted:";
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly OjsEncryptionServiceOptions _options;
    private readonly AesGcm? _aesGcm;
    private readonly HttpClient? _codecClient;

    /// <summary>
    /// Creates a new encryption service with the given options.
    /// </summary>
    /// <param name="options">Encryption configuration options.</param>
    public OjsEncryptionService(OjsEncryptionServiceOptions options)
    {
        _options = options;

        if (!string.IsNullOrEmpty(options.EncryptionKey))
        {
            var keyBytes = Convert.FromBase64String(options.EncryptionKey);
            _aesGcm = new AesGcm(keyBytes, TagSizeBytes);
        }

        if (!string.IsNullOrEmpty(options.CodecServerUrl))
        {
            _codecClient = new HttpClient { BaseAddress = new Uri(options.CodecServerUrl) };
        }
    }

    /// <summary>
    /// Encrypts a plaintext string using AES-256-GCM.
    /// Returns a prefixed base64 string in the format "ojs-encrypted:{base64(nonce||ciphertext||tag)}".
    /// </summary>
    /// <param name="plaintext">The value to encrypt.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The encrypted value with the ojs-encrypted: prefix.</returns>
    public Task<string> EncryptAsync(string plaintext, CancellationToken ct = default)
    {
        if (_aesGcm is null)
            throw new InvalidOperationException("No encryption key configured");

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];

        _aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Wire format: nonce (12) || ciphertext (N) || tag (16)
        var result = new byte[NonceSizeBytes + ciphertext.Length + TagSizeBytes];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSizeBytes, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSizeBytes + ciphertext.Length, TagSizeBytes);

        return Task.FromResult(EncryptedPrefix + Convert.ToBase64String(result));
    }

    /// <summary>
    /// Decrypts a value previously encrypted by this service.
    /// </summary>
    /// <param name="encryptedValue">The encrypted value with ojs-encrypted: prefix.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The decrypted plaintext string.</returns>
    public Task<string> DecryptAsync(string encryptedValue, CancellationToken ct = default)
    {
        if (_aesGcm is null)
            throw new InvalidOperationException("No encryption key configured");

        if (!encryptedValue.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            throw new ArgumentException("Value is not encrypted (missing ojs-encrypted: prefix)");

        var data = Convert.FromBase64String(encryptedValue[EncryptedPrefix.Length..]);
        var nonce = data.AsSpan(0, NonceSizeBytes);
        var ciphertext = data.AsSpan(NonceSizeBytes, data.Length - NonceSizeBytes - TagSizeBytes);
        var tag = data.AsSpan(data.Length - TagSizeBytes);
        var plaintext = new byte[ciphertext.Length];

        _aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return Task.FromResult(Encoding.UTF8.GetString(plaintext));
    }

    /// <summary>
    /// Checks if a value appears to be encrypted (has the ojs-encrypted: prefix).
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True if the value starts with the ojs-encrypted: prefix.</returns>
    public bool IsEncrypted(string value) =>
        value?.StartsWith(EncryptedPrefix, StringComparison.Ordinal) == true;

    /// <inheritdoc />
    public void Dispose()
    {
        _aesGcm?.Dispose();
        _codecClient?.Dispose();
    }
}

/// <summary>
/// Options for the encryption service.
/// </summary>
public class OjsEncryptionServiceOptions
{
    /// <summary>Base64-encoded 256-bit encryption key for local AES-256-GCM encryption.</summary>
    public string? EncryptionKey { get; set; }

    /// <summary>URL of the OJS codec server for remote encryption/decryption.</summary>
    public string? CodecServerUrl { get; set; }

    /// <summary>Whether to encrypt job arguments by default.</summary>
    public bool EncryptByDefault { get; set; } = false;

    /// <summary>Job types whose arguments should always be encrypted.</summary>
    public string[] SensitiveJobTypes { get; set; } = [];
}
