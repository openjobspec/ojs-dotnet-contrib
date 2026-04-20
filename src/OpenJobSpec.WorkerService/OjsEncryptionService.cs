namespace OpenJobSpec.WorkerService;

/// <summary>
/// Service that handles encryption/decryption of job arguments.
/// Uses AES-256-GCM for local encryption or can delegate to an OJS codec server.
/// </summary>
public sealed class OjsEncryptionService : IDisposable
{
    private readonly OjsLocalAes256GcmCodec? _localCodec;
    private readonly OjsRemoteCodecClient? _remoteCodecClient;

    /// <summary>
    /// Creates a new encryption service with the given options.
    /// </summary>
    /// <param name="options">Encryption configuration options.</param>
    public OjsEncryptionService(OjsEncryptionServiceOptions options)
    {
        if (!string.IsNullOrEmpty(options.EncryptionKey))
        {
            _localCodec = new OjsLocalAes256GcmCodec(options.EncryptionKey);
        }

        if (!string.IsNullOrEmpty(options.CodecServerUrl))
        {
            _remoteCodecClient = new OjsRemoteCodecClient(options.CodecServerUrl);
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
        if (_localCodec is null)
            throw new InvalidOperationException("No encryption key configured");

        return Task.FromResult(_localCodec.Encrypt(plaintext));
    }

    /// <summary>
    /// Decrypts a value previously encrypted by this service.
    /// </summary>
    /// <param name="encryptedValue">The encrypted value with ojs-encrypted: prefix.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The decrypted plaintext string.</returns>
    public Task<string> DecryptAsync(string encryptedValue, CancellationToken ct = default)
    {
        if (_localCodec is null)
            throw new InvalidOperationException("No encryption key configured");

        return Task.FromResult(_localCodec.Decrypt(encryptedValue));
    }

    /// <summary>
    /// Checks if a value appears to be encrypted (has the ojs-encrypted: prefix).
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True if the value starts with the ojs-encrypted: prefix.</returns>
    public bool IsEncrypted(string value) =>
        OjsLocalAes256GcmCodec.IsEncrypted(value);

    /// <inheritdoc />
    public void Dispose()
    {
        _localCodec?.Dispose();
        _remoteCodecClient?.Dispose();
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
