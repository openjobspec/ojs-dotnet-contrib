using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenJobSpec.AspNetCore;

namespace OpenJobSpec.AspNetCore.Tests;

public class OjsEncryptionTests
{
    [Fact]
    public void OjsEncryptionOptions_DefaultValues_AreCorrect()
    {
        var options = new OjsEncryptionOptions();

        Assert.Null(options.EncryptionKey);
        Assert.Null(options.CodecServerUrl);
        Assert.False(options.EncryptByDefault);
        Assert.Empty(options.SensitiveJobTypes);
    }

    [Fact]
    public void OjsEncryptionOptions_SensitiveJobTypes_DefaultIsEmptyArray()
    {
        var options = new OjsEncryptionOptions();

        Assert.NotNull(options.SensitiveJobTypes);
        Assert.Empty(options.SensitiveJobTypes);
    }

    [Fact]
    public void OjsEncryptionOptions_CustomValues_ArePreserved()
    {
        var options = new OjsEncryptionOptions
        {
            EncryptionKey = "dGVzdC1rZXktMTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM=",
            CodecServerUrl = "http://codec:9090",
            EncryptByDefault = true,
            SensitiveJobTypes = ["payment.process", "pii.export"],
        };

        Assert.Equal("dGVzdC1rZXktMTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM=", options.EncryptionKey);
        Assert.Equal("http://codec:9090", options.CodecServerUrl);
        Assert.True(options.EncryptByDefault);
        Assert.Equal(2, options.SensitiveJobTypes.Length);
        Assert.Contains("payment.process", options.SensitiveJobTypes);
        Assert.Contains("pii.export", options.SensitiveJobTypes);
    }

    [Fact]
    public void AddOjsEncryption_RegistersOptions()
    {
        var services = new ServiceCollection();
        services.AddOjsEncryption(opts =>
        {
            opts.EncryptByDefault = true;
            opts.SensitiveJobTypes = ["secret.job"];
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OjsEncryptionOptions>>().Value;

        Assert.True(options.EncryptByDefault);
        Assert.Single(options.SensitiveJobTypes);
        Assert.Equal("secret.job", options.SensitiveJobTypes[0]);
    }

    [Fact]
    public void AddOjsEncryption_WithEncryptionKey_SetsKey()
    {
        var services = new ServiceCollection();
        services.AddOjsEncryption(opts =>
        {
            opts.EncryptionKey = "dGVzdC1rZXktdmFsdWU=";
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OjsEncryptionOptions>>().Value;

        Assert.Equal("dGVzdC1rZXktdmFsdWU=", options.EncryptionKey);
    }

    [Fact]
    public void AddOjsEncryption_WithCodecServerUrl_SetsUrl()
    {
        var services = new ServiceCollection();
        services.AddOjsEncryption(opts =>
        {
            opts.CodecServerUrl = "http://codec-server:8443";
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OjsEncryptionOptions>>().Value;

        Assert.Equal("http://codec-server:8443", options.CodecServerUrl);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip_ReturnsOriginal()
    {
        // Generate a valid 256-bit key
        var keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        var base64Key = Convert.ToBase64String(keyBytes);

        var original = """{"job":{"id":"123","type":"email.send","args":["test@example.com"]}}""";

        var encrypted = OjsAes256GcmPayloadCodec.Encrypt(original, base64Key);
        var decrypted = OjsAes256GcmPayloadCodec.Decrypt(encrypted, base64Key);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertexts_ForSameInput()
    {
        var keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        var base64Key = Convert.ToBase64String(keyBytes);

        var plaintext = "test data";

        var encrypted1 = OjsAes256GcmPayloadCodec.Encrypt(plaintext, base64Key);
        var encrypted2 = OjsAes256GcmPayloadCodec.Encrypt(plaintext, base64Key);

        // Different nonces should produce different ciphertexts
        Assert.NotEqual(encrypted1, encrypted2);

        // Both should decrypt to the same value
        Assert.Equal(plaintext, OjsAes256GcmPayloadCodec.Decrypt(encrypted1, base64Key));
        Assert.Equal(plaintext, OjsAes256GcmPayloadCodec.Decrypt(encrypted2, base64Key));
    }

    [Fact]
    public void Decrypt_WithWrongKey_Throws()
    {
        var keyBytes1 = new byte[32];
        var keyBytes2 = new byte[32];
        RandomNumberGenerator.Fill(keyBytes1);
        RandomNumberGenerator.Fill(keyBytes2);

        var encrypted = OjsAes256GcmPayloadCodec.Encrypt("secret", Convert.ToBase64String(keyBytes1));

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => OjsAes256GcmPayloadCodec.Decrypt(encrypted, Convert.ToBase64String(keyBytes2)));
    }

    [Fact]
    public void Decrypt_KnownAes256GcmPayload_PreservesWireFormat()
    {
        const string key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
        const string payload = "AAECAwQFBgcICQoLPCC8dKfH+GCvKPOpi8tJX7D0qxaEAi8ZGl3H4HAIad4vY8uSy+Nv5R2gjZzGuMCdOtuzUEJmjBY=";

        var plaintext = OjsAes256GcmPayloadCodec.Decrypt(payload, key);

        Assert.Equal("""{"job":{"id":"123","type":"email.send"}}""", plaintext);
    }

    [Fact]
    public void Encrypt_PayloadUsesNonceCiphertextTagLayout()
    {
        var keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        const string plaintext = "héllo encrypted payload";

        var encrypted = OjsAes256GcmPayloadCodec.Encrypt(
            plaintext,
            Convert.ToBase64String(keyBytes));

        Assert.Equal(
            12 + Encoding.UTF8.GetByteCount(plaintext) + 16,
            Convert.FromBase64String(encrypted).Length);
    }

    [Fact]
    public void Decrypt_PayloadShorterThanNonceAndTag_ThrowsExactError()
    {
        var key = Convert.ToBase64String(new byte[32]);
        var payload = Convert.ToBase64String(new byte[27]);

        var exception = Assert.Throws<CryptographicException>(
            () => OjsAes256GcmPayloadCodec.Decrypt(payload, key));

        Assert.Equal("Encrypted payload is too short", exception.Message);
    }

    [Fact]
    public async Task Middleware_NoEncryptionConfiguration_PassesOriginalRequestThrough()
    {
        var originalBody = new MemoryStream(Encoding.UTF8.GetBytes("""{"job":{"id":"plain"}}"""));
        var context = CreateContext("POST", "/ojs/webhook", originalBody);
        Stream? downstreamBody = null;

        var middleware = CreateMiddleware(
            _ =>
            {
                downstreamBody = context.Request.Body;
                return Task.CompletedTask;
            },
            new OjsEncryptionOptions());

        await middleware.InvokeAsync(context);

        Assert.Same(originalBody, downstreamBody);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_PlainOjsPost_BuffersAndRewindsExactBody()
    {
        const string body = """{"job":{"id":"plain","type":"email.send"}}""";
        var originalBody = new NonSeekableReadStream(Encoding.UTF8.GetBytes(body));
        var context = CreateContext("POST", "/api/ojs/webhook", originalBody);
        string? downstreamBody = null;

        var middleware = CreateMiddleware(
            async downstreamContext =>
            {
                Assert.True(downstreamContext.Request.Body.CanSeek);
                using var reader = new StreamReader(
                    downstreamContext.Request.Body,
                    Encoding.UTF8,
                    leaveOpen: true);
                downstreamBody = await reader.ReadToEndAsync();
            },
            new OjsEncryptionOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) });

        await middleware.InvokeAsync(context);

        Assert.Equal(body, downstreamBody);
        Assert.NotSame(originalBody, context.Request.Body);
        Assert.True(context.Request.Body.CanSeek);
        Assert.Null(context.Request.ContentLength);
    }

    [Fact]
    public async Task Middleware_EncryptedOjsPost_ReplacesBodyWithExactPlaintextBytes()
    {
        const string key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
        const string payload = "AAECAwQFBgcICQoLPCC8dKfH+GCvKPOpi8tJX7D0qxaEAi8ZGl3H4HAIad4vY8uSy+Nv5R2gjZzGuMCdOtuzUEJmjBY=";
        const string plaintext = """{"job":{"id":"123","type":"email.send"}}""";
        var context = CreateContext(
            "POST",
            "/ojs/webhook",
            new MemoryStream(Encoding.UTF8.GetBytes(
                $$"""{"encrypted":true,"ciphertext":"{{payload}}"}""")));
        byte[]? downstreamBytes = null;

        var middleware = CreateMiddleware(
            async downstreamContext =>
            {
                using var copy = new MemoryStream();
                await downstreamContext.Request.Body.CopyToAsync(copy);
                downstreamBytes = copy.ToArray();
            },
            new OjsEncryptionOptions { EncryptionKey = key });

        await middleware.InvokeAsync(context);

        Assert.Equal(Encoding.UTF8.GetBytes(plaintext), downstreamBytes);
        Assert.Equal(Encoding.UTF8.GetByteCount(plaintext), context.Request.ContentLength);
    }

    [Fact]
    public async Task Middleware_DecryptionFailure_ReturnsExactErrorWithoutCallingNext()
    {
        var context = CreateContext(
            "POST",
            "/ojs/webhook",
            new MemoryStream(Encoding.UTF8.GetBytes(
                """{"encrypted":true,"ciphertext":"not-base64"}""")));
        var nextCalled = false;

        var middleware = CreateMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            new OjsEncryptionOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) });

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var actual = JsonNode.Parse(await reader.ReadToEndAsync());
        var expected = JsonNode.Parse("""
            {
              "error": {
                "code": "decryption_failed",
                "message": "Failed to decrypt job payload"
              }
            }
            """);
        Assert.True(JsonNode.DeepEquals(expected, actual));
    }

    [Fact]
    public void OjsEncryptionOptions_SensitiveJobTypes_CanBeModified()
    {
        var options = new OjsEncryptionOptions
        {
            SensitiveJobTypes = ["job.one", "job.two", "job.three"],
        };

        Assert.Equal(3, options.SensitiveJobTypes.Length);
        Assert.Contains("job.two", options.SensitiveJobTypes);
    }

    private static OjsEncryptionMiddleware CreateMiddleware(
        RequestDelegate next,
        OjsEncryptionOptions options)
    {
        return new OjsEncryptionMiddleware(
            next,
            Options.Create(options),
            NullLogger<OjsEncryptionMiddleware>.Instance);
    }

    private static DefaultHttpContext CreateContext(string method, string path, Stream body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Body = body;
        context.Response.Body = new MemoryStream();
        return context;
    }
}

internal sealed class NonSeekableReadStream(byte[] bytes) : Stream
{
    private readonly MemoryStream _inner = new(bytes);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        _inner.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
