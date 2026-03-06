using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenJobSpec.WorkerService;

namespace OpenJobSpec.WorkerService.Tests;

public class OjsWorkerServiceEnvironmentTests
{
    [Fact]
    public void EnvironmentVariable_OverridesBaseUrl()
    {
        var original = Environment.GetEnvironmentVariable("OJS_URL");
        try
        {
            Environment.SetEnvironmentVariable("OJS_URL", "http://env-server:9999");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BaseUrl"] = "http://config-server:8080",
                })
                .Build();

            var builder = Host.CreateApplicationBuilder();
            builder.AddOjsWorker(config);

            var provider = builder.Services.BuildServiceProvider();
            var options = provider.GetRequiredService<OjsWorkerServiceOptions>();

            Assert.Equal("http://env-server:9999", options.BaseUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OJS_URL", original);
        }
    }

    [Fact]
    public void EnvironmentVariable_OverridesAuthToken()
    {
        var original = Environment.GetEnvironmentVariable("OJS_AUTH_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("OJS_AUTH_TOKEN", "env-token-secret");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BaseUrl"] = "http://test:8080",
                })
                .Build();

            var builder = Host.CreateApplicationBuilder();
            builder.AddOjsWorker(config);

            var provider = builder.Services.BuildServiceProvider();
            var options = provider.GetRequiredService<OjsWorkerServiceOptions>();

            Assert.Equal("env-token-secret", options.AuthToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OJS_AUTH_TOKEN", original);
        }
    }

    [Fact]
    public void EnvironmentVariable_OverridesQueues()
    {
        var original = Environment.GetEnvironmentVariable("OJS_QUEUES");
        try
        {
            Environment.SetEnvironmentVariable("OJS_QUEUES", "emails, reports , priority");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BaseUrl"] = "http://test:8080",
                })
                .Build();

            var builder = Host.CreateApplicationBuilder();
            builder.AddOjsWorker(config);

            var provider = builder.Services.BuildServiceProvider();
            var options = provider.GetRequiredService<OjsWorkerServiceOptions>();

            Assert.Equal(new[] { "emails", "reports", "priority" }, options.Queues);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OJS_QUEUES", original);
        }
    }

    [Fact]
    public void EnvironmentVariable_OverridesConcurrency()
    {
        var original = Environment.GetEnvironmentVariable("OJS_CONCURRENCY");
        try
        {
            Environment.SetEnvironmentVariable("OJS_CONCURRENCY", "42");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BaseUrl"] = "http://test:8080",
                })
                .Build();

            var builder = Host.CreateApplicationBuilder();
            builder.AddOjsWorker(config);

            var provider = builder.Services.BuildServiceProvider();
            var options = provider.GetRequiredService<OjsWorkerServiceOptions>();

            Assert.Equal(42, options.Concurrency);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OJS_CONCURRENCY", original);
        }
    }

    [Fact]
    public void EnvironmentVariable_InvalidConcurrency_KeepsConfigValue()
    {
        var original = Environment.GetEnvironmentVariable("OJS_CONCURRENCY");
        try
        {
            Environment.SetEnvironmentVariable("OJS_CONCURRENCY", "invalid");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BaseUrl"] = "http://test:8080",
                    ["Concurrency"] = "15",
                })
                .Build();

            var builder = Host.CreateApplicationBuilder();
            builder.AddOjsWorker(config);

            var provider = builder.Services.BuildServiceProvider();
            var options = provider.GetRequiredService<OjsWorkerServiceOptions>();

            Assert.Equal(15, options.Concurrency);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OJS_CONCURRENCY", original);
        }
    }
}
