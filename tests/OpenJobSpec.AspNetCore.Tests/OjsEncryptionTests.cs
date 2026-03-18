using Microsoft.Extensions.DependencyInjection;
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
        System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);
        var base64Key = Convert.ToBase64String(keyBytes);

        var original = """{"job":{"id":"123","type":"email.send","args":["test@example.com"]}}""";

        var encrypted = OjsEncryptionMiddleware.EncryptAes256Gcm(original, base64Key);
        var decrypted = OjsEncryptionMiddleware.DecryptAes256Gcm(encrypted, base64Key);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertexts_ForSameInput()
    {
        var keyBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);
        var base64Key = Convert.ToBase64String(keyBytes);

        var plaintext = "test data";

        var encrypted1 = OjsEncryptionMiddleware.EncryptAes256Gcm(plaintext, base64Key);
        var encrypted2 = OjsEncryptionMiddleware.EncryptAes256Gcm(plaintext, base64Key);

        // Different nonces should produce different ciphertexts
        Assert.NotEqual(encrypted1, encrypted2);

        // Both should decrypt to the same value
        Assert.Equal(plaintext, OjsEncryptionMiddleware.DecryptAes256Gcm(encrypted1, base64Key));
        Assert.Equal(plaintext, OjsEncryptionMiddleware.DecryptAes256Gcm(encrypted2, base64Key));
    }

    [Fact]
    public void Decrypt_WithWrongKey_Throws()
    {
        var keyBytes1 = new byte[32];
        var keyBytes2 = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes1);
        System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes2);

        var encrypted = OjsEncryptionMiddleware.EncryptAes256Gcm("secret", Convert.ToBase64String(keyBytes1));

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => OjsEncryptionMiddleware.DecryptAes256Gcm(encrypted, Convert.ToBase64String(keyBytes2)));
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
}
