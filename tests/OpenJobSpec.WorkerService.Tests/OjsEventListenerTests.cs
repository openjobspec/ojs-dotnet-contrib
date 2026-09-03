using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenJobSpec;
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

    [Fact]
    public async Task ListenerService_FiltersMapsScopesAndIsolatesListenerFailures()
    {
        var tracker = new EventDispatchTracker();
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts =>
        {
            opts.BaseUrl = "http://test:8080";
            opts.EnableHealthCheck = false;
            opts.EventListener.Enabled = true;
            opts.EventListener.EventTypes = ["job.completed"];
        });
        builder.Services.AddSingleton(tracker);
        builder.Services.AddScoped<ScopedEventListenerDependency>();
        builder.Services.AddTransient<IOjsEventListener, ThrowingCompletedEventListener>();
        builder.Services.AddTransient<IOjsEventListener, CapturingCompletedEventListener>();

        using var provider = builder.Services.BuildServiceProvider();
        var worker = provider.GetRequiredService<OJSWorker>();
        var service = provider.GetServices<IHostedService>().OfType<OjsEventListenerService>().Single();
        var metadata = new Dictionary<string, object?>
        {
            ["type"] = "email.send",
            ["state"] = "completed",
            ["attempt"] = 3,
            ["queue"] = "critical",
            ["custom"] = "value",
        };

        await service.StartAsync(CancellationToken.None);
        try
        {
            await EmitAsync(worker, new OJSEvent
            {
                Id = "ignored-event",
                Type = "job.failed",
                Source = "test",
                Subject = "job-ignored",
                Time = DateTimeOffset.UtcNow,
                Data = metadata,
            });
            await EmitAsync(worker, new OJSEvent
            {
                Id = "delivered-event",
                Type = "job.completed",
                Source = "test",
                Subject = "job-123",
                Time = new DateTimeOffset(2026, 8, 3, 1, 2, 3, TimeSpan.Zero),
                Data = metadata,
            });
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Equal(1, tracker.ThrowingListenerCalls);
        Assert.Equal(1, tracker.CapturingListenerCalls);
        Assert.Equal(1, tracker.DisposedDependencyCount);
        Assert.True(tracker.CancellationCanBeCanceled);

        var eventData = Assert.IsType<OjsEventData>(tracker.EventData);
        Assert.Equal("job.completed", eventData.EventType);
        Assert.Equal("job-123", eventData.JobId);
        Assert.Equal("email.send", eventData.JobType);
        Assert.Equal("completed", eventData.State);
        Assert.Equal(new DateTimeOffset(2026, 8, 3, 1, 2, 3, TimeSpan.Zero), eventData.Timestamp);
        Assert.Equal(3, eventData.Attempt);
        Assert.Equal("critical", eventData.Queue);
        Assert.Same(metadata, eventData.Metadata);
    }

    [Fact]
    public void ListenerService_ConstructorDoesNotRequireUnusedClient()
    {
        var constructor = Assert.Single(typeof(OjsEventListenerService).GetConstructors());
        var parameterTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType);

        Assert.DoesNotContain(typeof(OJSClient), parameterTypes);
    }

    private static async Task EmitAsync(OJSWorker worker, OJSEvent evt)
    {
        var emitAsync = typeof(OJSEventEmitter).GetMethod(
            "EmitAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(emitAsync);

        var task = emitAsync.Invoke(worker.Events, [evt]);
        await Assert.IsAssignableFrom<Task>(task);
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

internal sealed class EventDispatchTracker
{
    public int ThrowingListenerCalls { get; set; }
    public int CapturingListenerCalls { get; set; }
    public int DisposedDependencyCount { get; set; }
    public bool CancellationCanBeCanceled { get; set; }
    public OjsEventData? EventData { get; set; }
}

internal sealed class ScopedEventListenerDependency(EventDispatchTracker tracker) : IDisposable
{
    public void Dispose()
    {
        tracker.DisposedDependencyCount++;
    }
}

internal sealed class ThrowingCompletedEventListener(
    EventDispatchTracker tracker,
    ScopedEventListenerDependency dependency) : IOjsEventListener
{
    public string EventType => "job.completed";

    public Task HandleAsync(OjsEventData eventData, CancellationToken ct = default)
    {
        GC.KeepAlive(dependency);
        tracker.ThrowingListenerCalls++;
        throw new InvalidOperationException("Expected listener failure");
    }
}

internal sealed class CapturingCompletedEventListener(
    EventDispatchTracker tracker,
    ScopedEventListenerDependency dependency) : IOjsEventListener
{
    public string EventType => "job.completed";

    public Task HandleAsync(OjsEventData eventData, CancellationToken ct = default)
    {
        GC.KeepAlive(dependency);
        tracker.CapturingListenerCalls++;
        tracker.CancellationCanBeCanceled = ct.CanBeCanceled;
        tracker.EventData = eventData;
        return Task.CompletedTask;
    }
}
