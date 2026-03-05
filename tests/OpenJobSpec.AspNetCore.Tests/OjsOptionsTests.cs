using OpenJobSpec.AspNetCore;

namespace OpenJobSpec.AspNetCore.Tests;

public class OjsOptionsTests
{
    [Fact]
    public void DefaultBaseUrl_IsLocalhost()
    {
        var options = new OjsOptions();
        Assert.Equal("http://localhost:8080", options.BaseUrl);
    }

    [Fact]
    public void DefaultAuthToken_IsNull()
    {
        var options = new OjsOptions();
        Assert.Null(options.AuthToken);
    }

    [Fact]
    public void DefaultTimeout_Is30Seconds()
    {
        var options = new OjsOptions();
        Assert.Equal(30, options.TimeoutSeconds);
    }

    [Fact]
    public void DefaultWorkerQueues_ContainsDefault()
    {
        var options = new OjsOptions();
        Assert.Single(options.Worker.Queues);
        Assert.Equal("default", options.Worker.Queues[0]);
    }

    [Fact]
    public void DefaultWorkerConcurrency_Is10()
    {
        var options = new OjsOptions();
        Assert.Equal(10, options.Worker.Concurrency);
    }

    [Fact]
    public void DefaultWorkerPollInterval_Is2Seconds()
    {
        var options = new OjsOptions();
        Assert.Equal(2.0, options.Worker.PollIntervalSeconds);
    }

    [Fact]
    public void DefaultWorkerHeartbeatInterval_Is5Seconds()
    {
        var options = new OjsOptions();
        Assert.Equal(5.0, options.Worker.HeartbeatIntervalSeconds);
    }

    [Fact]
    public void DefaultWorkerShutdownTimeout_Is25Seconds()
    {
        var options = new OjsOptions();
        Assert.Equal(25.0, options.Worker.ShutdownTimeoutSeconds);
    }

    [Fact]
    public void CustomOptions_ArePreserved()
    {
        var options = new OjsOptions
        {
            BaseUrl = "http://ojs.example.com:9090",
            AuthToken = "secret-token",
            TimeoutSeconds = 60,
            Worker = new OjsWorkerOptions
            {
                Queues = ["emails", "notifications"],
                Concurrency = 20,
                PollIntervalSeconds = 1.0,
                HeartbeatIntervalSeconds = 10.0,
                ShutdownTimeoutSeconds = 30.0,
            },
        };

        Assert.Equal("http://ojs.example.com:9090", options.BaseUrl);
        Assert.Equal("secret-token", options.AuthToken);
        Assert.Equal(60, options.TimeoutSeconds);
        Assert.Equal(2, options.Worker.Queues.Length);
        Assert.Equal(20, options.Worker.Concurrency);
    }
}
