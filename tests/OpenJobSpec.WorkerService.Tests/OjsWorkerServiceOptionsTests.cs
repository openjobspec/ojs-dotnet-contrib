using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenJobSpec.WorkerService;

namespace OpenJobSpec.WorkerService.Tests;

public class OjsWorkerServiceOptionsTests
{
    [Fact]
    public void DefaultBaseUrl_IsLocalhost()
    {
        var options = new OjsWorkerServiceOptions();
        Assert.Equal("http://localhost:8080", options.BaseUrl);
    }

    [Fact]
    public void DefaultAuthToken_IsNull()
    {
        var options = new OjsWorkerServiceOptions();
        Assert.Null(options.AuthToken);
    }

    [Fact]
    public void DefaultQueues_ContainsDefault()
    {
        var options = new OjsWorkerServiceOptions();
        Assert.Single(options.Queues);
        Assert.Equal("default", options.Queues[0]);
    }

    [Fact]
    public void DefaultConcurrency_Is10()
    {
        var options = new OjsWorkerServiceOptions();
        Assert.Equal(10, options.Concurrency);
    }

    [Fact]
    public void DefaultPollInterval_Is2Seconds()
    {
        var options = new OjsWorkerServiceOptions();
        Assert.Equal(2.0, options.PollIntervalSeconds);
    }

    [Fact]
    public void DefaultShutdownTimeout_Is25Seconds()
    {
        var options = new OjsWorkerServiceOptions();
        Assert.Equal(25.0, options.ShutdownTimeoutSeconds);
    }

    [Fact]
    public void DefaultHealthCheck_IsEnabled()
    {
        var options = new OjsWorkerServiceOptions();
        Assert.True(options.EnableHealthCheck);
        Assert.Equal("ojs", options.HealthCheckName);
    }

    [Fact]
    public void CustomOptions_ArePreserved()
    {
        var options = new OjsWorkerServiceOptions
        {
            BaseUrl = "http://ojs.prod:9090",
            AuthToken = "secret",
            Queues = ["emails", "reports"],
            Concurrency = 20,
            PollIntervalSeconds = 1.0,
            ShutdownTimeoutSeconds = 60.0,
            EnableHealthCheck = false,
        };

        Assert.Equal("http://ojs.prod:9090", options.BaseUrl);
        Assert.Equal("secret", options.AuthToken);
        Assert.Equal(new[] { "emails", "reports" }, options.Queues);
        Assert.Equal(20, options.Concurrency);
        Assert.False(options.EnableHealthCheck);
    }
}
