using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenJobSpec.AspNetCore;

/// <summary>
/// Middleware that encrypts/decrypts job arguments using AES-256-GCM.
/// Integrates with the OJS codec server pattern for transparent payload encryption.
/// </summary>
internal sealed class OjsEncryptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly OjsEncryptionOptions _options;
    private readonly ILogger<OjsEncryptionMiddleware> _logger;

    public OjsEncryptionMiddleware(RequestDelegate next, IOptions<OjsEncryptionOptions> options, ILogger<OjsEncryptionMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_options.EncryptionKey is null && _options.CodecServerUrl is null)
        {
            await _next(context);
            return;
        }

        // Check if the request path matches OJS webhook or cron endpoints
        var path = context.Request.Path.Value ?? "";
        if (context.Request.Method == "POST" && IsOjsEndpoint(path))
        {
            context.Request.EnableBuffering();

            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            if (ContainsEncryptedPayload(body))
            {
                _logger.LogDebug("Decrypting payload for request to {Path}", path);

                try
                {
                    var decrypted = DecryptPayload(body);
                    var bytes = Encoding.UTF8.GetBytes(decrypted);
                    context.Request.Body = new MemoryStream(bytes);
                    context.Request.ContentLength = bytes.Length;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to decrypt payload for request to {Path}", path);
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = new { code = "decryption_failed", message = "Failed to decrypt job payload" },
                    });
                    return;
                }
            }
        }

        await _next(context);
    }

    private bool IsOjsEndpoint(string path) =>
        path.Contains("/ojs/", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsEncryptedPayload(string body) =>
        body.Contains("\"encrypted\"", StringComparison.OrdinalIgnoreCase) &&
        body.Contains("\"ciphertext\"", StringComparison.OrdinalIgnoreCase);

    private string DecryptPayload(string body)
    {
        if (_options.EncryptionKey is null)
            throw new InvalidOperationException("Encryption key is not configured");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("encrypted", out var encryptedProp) || !encryptedProp.GetBoolean())
            return body;

        var ciphertext = root.GetProperty("ciphertext").GetString()
            ?? throw new InvalidOperationException("Missing ciphertext");

        var decrypted = DecryptAes256Gcm(ciphertext, _options.EncryptionKey);
        return decrypted;
    }

    internal static string DecryptAes256Gcm(string base64Payload, string base64Key)
    {
        var payload = Convert.FromBase64String(base64Payload);
        var key = Convert.FromBase64String(base64Key);

        // Payload format: [12-byte nonce][ciphertext][16-byte tag]
        const int nonceSize = 12;
        const int tagSize = 16;

        if (payload.Length < nonceSize + tagSize)
            throw new CryptographicException("Encrypted payload is too short");

        var nonce = payload.AsSpan(0, nonceSize);
        var tag = payload.AsSpan(payload.Length - tagSize);
        var ciphertext = payload.AsSpan(nonceSize, payload.Length - nonceSize - tagSize);

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, tagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    internal static string EncryptAes256Gcm(string plaintext, string base64Key)
    {
        var key = Convert.FromBase64String(base64Key);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        const int nonceSize = 12;
        const int tagSize = 16;

        var nonce = new byte[nonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[tagSize];

        using var aes = new AesGcm(key, tagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Payload format: [nonce][ciphertext][tag]
        var payload = new byte[nonceSize + ciphertext.Length + tagSize];
        nonce.CopyTo(payload, 0);
        ciphertext.CopyTo(payload, nonceSize);
        tag.CopyTo(payload, nonceSize + ciphertext.Length);

        return Convert.ToBase64String(payload);
    }
}

/// <summary>
/// Configuration options for OJS payload encryption.
/// </summary>
public class OjsEncryptionOptions
{
    /// <summary>
    /// Base64-encoded AES-256 key for local encryption/decryption.
    /// Either this or <see cref="CodecServerUrl"/> must be set.
    /// </summary>
    public string? EncryptionKey { get; set; }

    /// <summary>
    /// URL of an external OJS codec server for encryption/decryption.
    /// </summary>
    public string? CodecServerUrl { get; set; }

    /// <summary>
    /// When true, all outgoing job payloads are encrypted by default.
    /// </summary>
    public bool EncryptByDefault { get; set; } = false;

    /// <summary>
    /// Job types whose payloads should always be encrypted,
    /// regardless of <see cref="EncryptByDefault"/>.
    /// </summary>
    public string[] SensitiveJobTypes { get; set; } = [];
}

/// <summary>
/// Extension methods for adding OJS encryption to the ASP.NET Core pipeline.
/// </summary>
public static class OjsEncryptionExtensions
{
    /// <summary>
    /// Registers OJS encryption services and options with the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure encryption options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOjsEncryption(this IServiceCollection services, Action<OjsEncryptionOptions> configure)
    {
        services.Configure(configure);
        return services;
    }

    /// <summary>
    /// Adds the OJS encryption middleware to the request pipeline.
    /// Must be called after <see cref="AddOjsEncryption"/>.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseOjsEncryption(this IApplicationBuilder app)
    {
        return app.UseMiddleware<OjsEncryptionMiddleware>();
    }
}
