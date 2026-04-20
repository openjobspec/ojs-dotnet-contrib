using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenJobSpec;
using OpenJobSpec.AspNetCore;

namespace OpenJobSpec.AspNetCore.Tests;

public class OjsEventSubscriptionServiceTests
{
    [Fact]
    public void AddOjsEventSubscription_RegistersService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsEventSubscription();

        var provider = services.BuildServiceProvider();
        var eventService = provider.GetService<OjsEventSubscriptionService>();

        Assert.NotNull(eventService);
    }

    [Fact]
    public void AddOjsEventSubscription_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsEventSubscription();

        var provider = services.BuildServiceProvider();
        var service1 = provider.GetService<OjsEventSubscriptionService>();
        var service2 = provider.GetService<OjsEventSubscriptionService>();

        Assert.NotNull(service1);
        Assert.Same(service1, service2);
    }

    [Fact]
    public void Subscribe_IncreasesSubscriptionCount()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsEventSubscription();

        var provider = services.BuildServiceProvider();
        var eventService = provider.GetRequiredService<OjsEventSubscriptionService>();

        Assert.Equal(0, eventService.SubscriptionCount);

        var sub = eventService.Subscribe("job.completed", _ => Task.CompletedTask);

        Assert.Equal(1, eventService.SubscriptionCount);
        sub.Dispose();
    }

    [Fact]
    public void Subscribe_DisposingSubscription_RemovesIt()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsEventSubscription();

        var provider = services.BuildServiceProvider();
        var eventService = provider.GetRequiredService<OjsEventSubscriptionService>();

        var sub = eventService.Subscribe("job.completed", _ => Task.CompletedTask);
        Assert.Equal(1, eventService.SubscriptionCount);

        sub.Dispose();
        Assert.Equal(0, eventService.SubscriptionCount);
    }

    [Fact]
    public void SubscribeToJobType_IncreasesSubscriptionCount()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsEventSubscription();

        var provider = services.BuildServiceProvider();
        var eventService = provider.GetRequiredService<OjsEventSubscriptionService>();

        var sub = eventService.SubscribeToJobType("email.send", _ => Task.CompletedTask);

        Assert.Equal(1, eventService.SubscriptionCount);
        sub.Dispose();
    }

    [Fact]
    public void MultipleSubscriptions_AllTracked()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsEventSubscription();

        var provider = services.BuildServiceProvider();
        var eventService = provider.GetRequiredService<OjsEventSubscriptionService>();

        var sub1 = eventService.Subscribe("job.completed", _ => Task.CompletedTask);
        var sub2 = eventService.Subscribe("job.failed", _ => Task.CompletedTask);
        var sub3 = eventService.SubscribeToJobType("email.send", _ => Task.CompletedTask);

        Assert.Equal(3, eventService.SubscriptionCount);

        sub1.Dispose();
        Assert.Equal(2, eventService.SubscriptionCount);

        sub2.Dispose();
        sub3.Dispose();
        Assert.Equal(0, eventService.SubscriptionCount);
    }

    [Fact]
    public void Dispose_CleansUpAllSubscriptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsEventSubscription();

        var provider = services.BuildServiceProvider();
        var eventService = provider.GetRequiredService<OjsEventSubscriptionService>();

        eventService.Subscribe("job.completed", _ => Task.CompletedTask);
        eventService.Subscribe("job.failed", _ => Task.CompletedTask);

        Assert.Equal(2, eventService.SubscriptionCount);

        eventService.Dispose();

        Assert.Equal(0, eventService.SubscriptionCount);
    }

    [Fact]
    public async Task SubscriptionDispose_IsIdempotentAndStopsCallbackDelivery()
    {
        using var provider = CreateProvider();
        var worker = provider.GetRequiredService<OJSWorker>();
        var eventService = provider.GetRequiredService<OjsEventSubscriptionService>();
        var callbackCount = 0;
        var subscription = eventService.Subscribe(
            "job.completed",
            _ =>
            {
                Interlocked.Increment(ref callbackCount);
                return Task.CompletedTask;
            });

        await EmitAsync(worker, "job.completed");
        subscription.Dispose();
        subscription.Dispose();
        await EmitAsync(worker, "job.completed");

        Assert.Equal(1, callbackCount);
        Assert.Equal(0, eventService.SubscriptionCount);
    }

    [Fact]
    public async Task ConcurrentSubscriptionAndServiceDisposal_LeavesNoActiveCallbacks()
    {
        using var provider = CreateProvider();
        var worker = provider.GetRequiredService<OJSWorker>();
        var eventService = provider.GetRequiredService<OjsEventSubscriptionService>();
        var callbackCount = 0;
        using var start = new ManualResetEventSlim();

        var subscribingTasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                for (var i = 0; i < 250; i++)
                {
                    try
                    {
                        eventService.Subscribe(
                            "job.completed",
                            _ =>
                            {
                                Interlocked.Increment(ref callbackCount);
                                return Task.CompletedTask;
                            });
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                }
            }))
            .ToArray();

        var disposeTask = Task.Run(() =>
        {
            start.Wait();
            eventService.Dispose();
        });

        start.Set();
        await Task.WhenAll(subscribingTasks.Append(disposeTask));
        await EmitAsync(worker, "job.completed");

        Assert.Equal(0, eventService.SubscriptionCount);
        Assert.Equal(0, callbackCount);
    }

    [Fact]
    public void OjsEvent_RecordProperties_ArePreserved()
    {
        var now = DateTimeOffset.UtcNow;
        var metadata = new Dictionary<string, object?> { ["key"] = "value" };
        var evt = new OjsEvent("job.completed", "job-123", "email.send", "completed", now, metadata);

        Assert.Equal("job.completed", evt.EventType);
        Assert.Equal("job-123", evt.JobId);
        Assert.Equal("email.send", evt.JobType);
        Assert.Equal("completed", evt.State);
        Assert.Equal(now, evt.Timestamp);
        Assert.NotNull(evt.Metadata);
        Assert.Equal("value", evt.Metadata["key"]);
    }

    [Fact]
    public void OjsEvent_DefaultMetadata_IsNull()
    {
        var evt = new OjsEvent("job.completed", "job-123", "email.send", "completed", DateTimeOffset.UtcNow);

        Assert.Null(evt.Metadata);
    }

    [Fact]
    public void OjsEvent_Equality_WorksByValue()
    {
        var now = DateTimeOffset.UtcNow;
        var evt1 = new OjsEvent("job.completed", "job-123", "email.send", "completed", now);
        var evt2 = new OjsEvent("job.completed", "job-123", "email.send", "completed", now);

        Assert.Equal(evt1, evt2);
    }

    [Fact]
    public async Task StartAsync_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsEventSubscription();

        var provider = services.BuildServiceProvider();
        var eventService = provider.GetRequiredService<OjsEventSubscriptionService>();

        await eventService.StartAsync();
    }

    [Fact]
    public async Task StopAsync_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsEventSubscription();

        var provider = services.BuildServiceProvider();
        var eventService = provider.GetRequiredService<OjsEventSubscriptionService>();

        eventService.Subscribe("job.completed", _ => Task.CompletedTask);

        await eventService.StopAsync();

        Assert.Equal(0, eventService.SubscriptionCount);
    }

    [Fact]
    public void AddOjsEventHandler_RegistersHandlerRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsEventSubscription();
        services.AddOjsEventHandler<TestEventHandler>();

        var provider = services.BuildServiceProvider();
        var registrations = provider.GetServices<OjsEventHandlerRegistration>();

        Assert.Contains(registrations, r => r.HandlerType == typeof(TestEventHandler));
    }

    [Fact]
    public void AddOjsEventHandler_RegistersHandlerAsTransient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsEventHandler<TestEventHandler>();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TestEventHandler));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsEventSubscription();
        return services.BuildServiceProvider();
    }

    private static async Task EmitAsync(OJSWorker worker, string eventType)
    {
        var emitAsync = typeof(OJSEventEmitter).GetMethod(
            "EmitAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(emitAsync);

        var task = emitAsync.Invoke(worker.Events, [
            new OJSEvent
            {
                Id = Guid.NewGuid().ToString(),
                Type = eventType,
                Source = "test",
                Time = DateTimeOffset.UtcNow,
                Data = new Dictionary<string, object?>
                {
                    ["job_id"] = "job-1",
                    ["job_type"] = "email.send",
                    ["state"] = "completed",
                },
            },
        ]);

        await Assert.IsAssignableFrom<Task>(task);
    }
}

internal class TestEventHandler : IOjsEventHandler
{
    public string EventType => "job.completed";

    public Task HandleAsync(OjsEvent evt, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
