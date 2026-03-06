using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenJobSpec.AspNetCore;

namespace OpenJobSpec.AspNetCore.Tests;

public class OjsConfigurationBindingTests
{
    [Fact]
    public void AddOjs_FromConfiguration_BindsBaseUrl()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BaseUrl"] = "http://ojs.example.com:9090",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOjs(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<OjsOptions>();

        Assert.Equal("http://ojs.example.com:9090", options.BaseUrl);
    }

    [Fact]
    public void AddOjs_FromConfiguration_BindsWorkerOptions()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BaseUrl"] = "http://test:8080",
                ["Worker:Queues:0"] = "high",
                ["Worker:Queues:1"] = "low",
                ["Worker:Concurrency"] = "20",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOjs(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<OjsOptions>();

        Assert.Equal(20, options.Worker.Concurrency);
    }

    [Fact]
    public void AddOjs_EnvironmentVariable_OverridesBaseUrl()
    {
        var originalUrl = Environment.GetEnvironmentVariable("OJS_URL");
        try
        {
            Environment.SetEnvironmentVariable("OJS_URL", "http://env-override:9999");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BaseUrl"] = "http://config-value:8080",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddOjs(config);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<OjsOptions>();

            Assert.Equal("http://env-override:9999", options.BaseUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OJS_URL", originalUrl);
        }
    }

    [Fact]
    public void AddOjs_EnvironmentVariable_OverridesAuthToken()
    {
        var originalToken = Environment.GetEnvironmentVariable("OJS_AUTH_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("OJS_AUTH_TOKEN", "env-secret-token");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BaseUrl"] = "http://test:8080",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddOjs(config);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<OjsOptions>();

            Assert.Equal("env-secret-token", options.AuthToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OJS_AUTH_TOKEN", originalToken);
        }
    }

    [Fact]
    public void AddOjs_EnvironmentVariable_OverridesQueues()
    {
        var originalQueues = Environment.GetEnvironmentVariable("OJS_QUEUES");
        try
        {
            Environment.SetEnvironmentVariable("OJS_QUEUES", "emails,notifications,reports");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BaseUrl"] = "http://test:8080",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddOjs(config);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<OjsOptions>();

            Assert.Equal(new[] { "emails", "notifications", "reports" }, options.Worker.Queues);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OJS_QUEUES", originalQueues);
        }
    }

    [Fact]
    public void AddOjs_EnvironmentVariable_OverridesConcurrency()
    {
        var originalConcurrency = Environment.GetEnvironmentVariable("OJS_CONCURRENCY");
        try
        {
            Environment.SetEnvironmentVariable("OJS_CONCURRENCY", "42");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BaseUrl"] = "http://test:8080",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddOjs(config);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<OjsOptions>();

            Assert.Equal(42, options.Worker.Concurrency);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OJS_CONCURRENCY", originalConcurrency);
        }
    }

    [Fact]
    public void AddOjs_EnvironmentVariable_InvalidConcurrency_KeepsDefault()
    {
        var originalConcurrency = Environment.GetEnvironmentVariable("OJS_CONCURRENCY");
        try
        {
            Environment.SetEnvironmentVariable("OJS_CONCURRENCY", "not-a-number");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BaseUrl"] = "http://test:8080",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddOjs(config);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<OjsOptions>();

            Assert.Equal(10, options.Worker.Concurrency); // default value
        }
        finally
        {
            Environment.SetEnvironmentVariable("OJS_CONCURRENCY", originalConcurrency);
        }
    }
}
