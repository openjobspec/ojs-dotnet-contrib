using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenJobSpec.WorkerService;

namespace OpenJobSpec.WorkerService.Tests;

public class OjsEncryptionServiceTests
{
    [Fact]
    public void OjsEncryptionServiceOptions_Defaults()
    {
        var options = new OjsEncryptionServiceOptions();

        Assert.Null(options.EncryptionKey);
        Assert.Null(options.CodecServerUrl);
        Assert.False(options.EncryptByDefault);
        Assert.Empty(options.SensitiveJobTypes);
    }

    [Fact]
    public void SensitiveJobTypes_Configuration()
    {
        var options = new OjsEncryptionServiceOptions
        {
            SensitiveJobTypes = ["payment.process", "user.create"]
        };

        Assert.Equal(2, options.SensitiveJobTypes.Length);
        Assert.Contains("payment.process", options.SensitiveJobTypes);
        Assert.Contains("user.create", options.SensitiveJobTypes);
    }

    [Fact]
    public void AddOjsEncryption_RegistersService()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts => opts.BaseUrl = "http://test:8080");
        builder.Services.AddOjsEncryption(opts =>
        {
            opts.EncryptionKey = Convert.ToBase64String(new byte[32]);
        });

        var provider = builder.Services.BuildServiceProvider();
        var service = provider.GetService<OjsEncryptionService>();

        Assert.NotNull(service);
    }

    [Fact]
    public void EncryptionService_CreatedWithKey()
    {
        var key = new byte[32];
        Random.Shared.NextBytes(key);

        var options = new OjsEncryptionServiceOptions
        {
            EncryptionKey = Convert.ToBase64String(key)
        };

        using var service = new OjsEncryptionService(options);
        Assert.NotNull(service);
    }

    [Fact]
    public async Task EncryptDecrypt_RoundTrip()
    {
        var key = new byte[32];
        Random.Shared.NextBytes(key);

        var options = new OjsEncryptionServiceOptions
        {
            EncryptionKey = Convert.ToBase64String(key)
        };

        using var service = new OjsEncryptionService(options);

        var original = "Hello, OJS! Sensitive payment data.";
        var encrypted = await service.EncryptAsync(original);

        Assert.True(service.IsEncrypted(encrypted));
        Assert.StartsWith("ojs-encrypted:", encrypted);

        var decrypted = await service.DecryptAsync(encrypted);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public async Task EncryptDecrypt_EmptyString()
    {
        var key = new byte[32];
        Random.Shared.NextBytes(key);

        var options = new OjsEncryptionServiceOptions
        {
            EncryptionKey = Convert.ToBase64String(key)
        };

        using var service = new OjsEncryptionService(options);

        var encrypted = await service.EncryptAsync(string.Empty);
        var decrypted = await service.DecryptAsync(encrypted);

        Assert.Equal(string.Empty, decrypted);
    }

    [Fact]
    public async Task DecryptAsync_KnownAes256GcmWirePayload()
    {
        const string key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
        const string payload = "AAECAwQFBgcICQoLPCC8dKfH+GCvKPOpi8tJX7D0qxaEAi8ZGl3H4HAIad4vY8uSy+Nv5R2gjZzGuMCdOtuzUEJmjBY=";
        using var service = new OjsEncryptionService(new OjsEncryptionServiceOptions
        {
            EncryptionKey = key,
        });

        var plaintext = await service.DecryptAsync($"ojs-encrypted:{payload}");

        Assert.Equal("""{"job":{"id":"123","type":"email.send"}}""", plaintext);
    }

    [Fact]
    public void IsEncrypted_DetectsPrefix()
    {
        var options = new OjsEncryptionServiceOptions();
        using var service = new OjsEncryptionService(options);

        Assert.True(service.IsEncrypted("ojs-encrypted:abc123"));
        Assert.False(service.IsEncrypted("plain text"));
        Assert.False(service.IsEncrypted(""));
    }

    [Fact]
    public async Task EncryptAsync_ThrowsWithoutKey()
    {
        var options = new OjsEncryptionServiceOptions();
        using var service = new OjsEncryptionService(options);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EncryptAsync("test"));
    }

    [Fact]
    public async Task DecryptAsync_ThrowsWithoutKey()
    {
        var options = new OjsEncryptionServiceOptions();
        using var service = new OjsEncryptionService(options);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DecryptAsync("ojs-encrypted:abc123"));
    }

    [Fact]
    public async Task CodecServerOnly_PreservesNoLocalKeyErrors()
    {
        using var service = new OjsEncryptionService(new OjsEncryptionServiceOptions
        {
            CodecServerUrl = "https://codec.example.test/base/",
        });

        var encryptException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EncryptAsync("test"));
        var decryptException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DecryptAsync("ojs-encrypted:abc123"));

        Assert.Equal("No encryption key configured", encryptException.Message);
        Assert.Equal("No encryption key configured", decryptException.Message);
    }

    [Fact]
    public void InvalidCodecServerUrl_PreservesConstructorError()
    {
        var exception = Assert.Throws<UriFormatException>(() =>
            new OjsEncryptionService(new OjsEncryptionServiceOptions
            {
                CodecServerUrl = "relative/path",
            }));

        Assert.Equal("Invalid URI: The format of the URI could not be determined.", exception.Message);
    }

    [Fact]
    public async Task DecryptAsync_ThrowsOnNonEncryptedValue()
    {
        var key = new byte[32];
        Random.Shared.NextBytes(key);

        var options = new OjsEncryptionServiceOptions
        {
            EncryptionKey = Convert.ToBase64String(key)
        };

        using var service = new OjsEncryptionService(options);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.DecryptAsync("plain text"));
    }

    [Fact]
    public void EncryptionServiceOptions_EncryptByDefault()
    {
        var options = new OjsEncryptionServiceOptions
        {
            EncryptByDefault = true,
            EncryptionKey = Convert.ToBase64String(new byte[32])
        };

        Assert.True(options.EncryptByDefault);
    }

    [Fact]
    public void AddOjsEncryption_ServiceIsSingleton()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts => opts.BaseUrl = "http://test:8080");
        builder.Services.AddOjsEncryption(opts =>
        {
            opts.EncryptionKey = Convert.ToBase64String(new byte[32]);
        });

        var provider = builder.Services.BuildServiceProvider();
        var service1 = provider.GetService<OjsEncryptionService>();
        var service2 = provider.GetService<OjsEncryptionService>();

        Assert.Same(service1, service2);
    }

    [Fact]
    public void EncryptionService_DoesNotOwnMixedOptionsAesAndHttpState()
    {
        var fieldTypes = typeof(OjsEncryptionService)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .ToList();

        Assert.DoesNotContain(typeof(OjsEncryptionServiceOptions), fieldTypes);
        Assert.DoesNotContain(typeof(AesGcm), fieldTypes);
        Assert.DoesNotContain(typeof(HttpClient), fieldTypes);
    }
}
