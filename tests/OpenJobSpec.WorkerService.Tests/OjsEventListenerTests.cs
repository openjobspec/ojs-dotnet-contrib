using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenJobSpec.WorkerService;

namespace OpenJobSpec.WorkerService.Tests;

public class OjsEventListenerTests
{
    [Fact]
    public void OjsEventData_RecordProperties()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var data = new OjsEventData(
            EventType: "job.completed",
            JobId: "test-123",
            JobType: "email.send",
            State: "completed",
            Timestamp: timestamp,
            Attempt: 1,
            Queue: "default");

        Assert.Equal("job.completed", data.EventType);
        Assert.Equal("test-123", data.JobId);
        Assert.Equal("email.send", data.JobType);
        Assert.Equal("completed", data.State);
        Assert.Equal(timestamp, data.Timestamp);
        Assert.Equal(1, data.Attempt);
        Assert.Equal("default", data.Queue);
        Assert.Null(data.Metadata);
    }

    [Fact]
    public void OjsEventData_WithMetadata()
    {
        var metadata = new Dictionary<string, object?> { ["key"] = "value", ["count"] = 42 };
        var data = new OjsEventData(
            "job.failed", "id-1", "report.generate", "failed",
            DateTimeOffset.UtcNow, 3, "high", metadata);

        Assert.NotNull(data.Metadata);
        Assert.Equal("value", data.Metadata["key"]);
        Assert.Equal(42, data.Metadata["count"]);
    }

    [Fact]
    public void OjsEventListenerOptions_Defaults()
    {
        var options = new OjsEventListenerOptions();

        Assert.False(options.Enabled);
        Assert.Equal(5.0, options.PollIntervalSeconds);
        Assert.Equal(3, options.EventTypes.Length);
        Assert.Contains("job.completed", options.EventTypes);
        Assert.Contains("job.failed", options.EventTypes);
        Assert.Contains("job.retrying", options.EventTypes);
    }

    [Fact]
    public void AddOjsEventListener_RegistersListener()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts => opts.BaseUrl = "http://test:8080");
        builder.Services.AddOjsEventListener<TestCompletedEventListener>();

        var provider = builder.Services.BuildServiceProvider();
        var listeners = provider.GetServices<IOjsEventListener>().ToList();

        Assert.Single(listeners);
        Assert.Equal("job.completed", listeners[0].EventType);
    }

    [Fact]
    public void AddOjsEventListener_MultipleListeners_ForDifferentEventTypes()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts => opts.BaseUrl = "http://test:8080");
        builder.Services.AddOjsEventListener<TestCompletedEventListener>();
        builder.Services.AddOjsEventListener<TestFailedEventListener>();

        var provider = builder.Services.BuildServiceProvider();
        var listeners = provider.GetServices<IOjsEventListener>().ToList();

        Assert.Equal(2, listeners.Count);
        Assert.Contains(listeners, l => l.EventType == "job.completed");
        Assert.Contains(listeners, l => l.EventType == "job.failed");
    }

    [Fact]
    public void EventTypeFiltering_MatchesConfiguredTypes()
    {
        var options = new OjsEventListenerOptions
        {
            EventTypes = ["job.completed", "job.cancelled"]
        };

        var eventTypes = new HashSet<string>(options.EventTypes);

        Assert.Contains("job.completed", eventTypes);
        Assert.Contains("job.cancelled", eventTypes);
        Assert.DoesNotContain("job.failed", eventTypes);
    }
}

internal class TestCompletedEventListener : IOjsEventListener
{
    public string EventType => "job.completed";
    public Task HandleAsync(OjsEventData eventData, CancellationToken ct = default) => Task.CompletedTask;
}

internal class TestFailedEventListener : IOjsEventListener
{
    public string EventType => "job.failed";
    public Task HandleAsync(OjsEventData eventData, CancellationToken ct = default) => Task.CompletedTask;
}
