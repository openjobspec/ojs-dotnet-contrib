using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OpenJobSpec;

namespace OpenJobSpec.AspNetCore;

/// <summary>
/// Service for subscribing to OJS events (job state changes, workflow events).
/// Supports both polling and callback patterns.
/// </summary>
public sealed class OjsEventSubscriptionService : IDisposable
{
    private readonly OJSWorker _worker;
    private readonly ILogger<OjsEventSubscriptionService> _logger;
    private readonly ConcurrentDictionary<Guid, SubscriptionEntry> _subscriptions = new();
    private readonly ConcurrentDictionary<Guid, Action> _unsubscribeActions = new();
    private bool _disposed;

    public OjsEventSubscriptionService(OJSWorker worker, ILogger<OjsEventSubscriptionService> logger)
    {
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Subscribe to events of a specific type with a callback handler.
    /// </summary>
    /// <param name="eventType">The event type to subscribe to (e.g., "job.completed").</param>
    /// <param name="handler">Async callback invoked when a matching event occurs.</param>
    /// <returns>An <see cref="IDisposable"/> that removes the subscription when disposed.</returns>
    public IDisposable Subscribe(string eventType, Func<OjsEvent, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(handler);

        ObjectDisposedException.ThrowIf(_disposed, this);

        var subscriptionId = Guid.NewGuid();
        var entry = new SubscriptionEntry(eventType, null, handler);
        _subscriptions[subscriptionId] = entry;

        var unsubscribe = _worker.Events.On(eventType, async evt =>
        {
            var ojsEvent = MapEvent(evt);
            await handler(ojsEvent);
        });

        _unsubscribeActions[subscriptionId] = unsubscribe;
        _logger.LogDebug("Subscribed to event type '{EventType}' (subscription: {SubscriptionId})", eventType, subscriptionId);

        return new Subscription(this, subscriptionId);
    }

    /// <summary>
    /// Subscribe to events for a specific job type across all event types.
    /// Only events whose subject matches the given job type are forwarded.
    /// </summary>
    /// <param name="jobType">The job type to filter events for (e.g., "email.send").</param>
    /// <param name="handler">Async callback invoked when a matching event occurs.</param>
    /// <returns>An <see cref="IDisposable"/> that removes the subscription when disposed.</returns>
    public IDisposable SubscribeToJobType(string jobType, Func<OjsEvent, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobType);
        ArgumentNullException.ThrowIfNull(handler);

        ObjectDisposedException.ThrowIf(_disposed, this);

        var subscriptionId = Guid.NewGuid();
        var entry = new SubscriptionEntry(null, jobType, handler);
        _subscriptions[subscriptionId] = entry;

        var unsubscribe = _worker.Events.OnAny(async evt =>
        {
            var data = evt.Data;
            if (data is not null && data.TryGetValue("job_type", out var jt) && jt?.ToString() == jobType)
            {
                var ojsEvent = MapEvent(evt);
                await handler(ojsEvent);
            }
        });

        _unsubscribeActions[subscriptionId] = unsubscribe;
        _logger.LogDebug("Subscribed to job type '{JobType}' events (subscription: {SubscriptionId})", jobType, subscriptionId);

        return new Subscription(this, subscriptionId);
    }

    /// <summary>
    /// Start listening for events. The worker must already be started for events to flow.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logger.LogInformation("OJS event subscription service started with {Count} subscriptions", _subscriptions.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop listening for events and clean up subscriptions.
    /// </summary>
    public Task StopAsync()
    {
        _logger.LogInformation("OJS event subscription service stopping");
        RemoveAllSubscriptions();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the current number of active subscriptions.
    /// </summary>
    public int SubscriptionCount => _subscriptions.Count;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        RemoveAllSubscriptions();
    }

    private void RemoveSubscription(Guid subscriptionId)
    {
        if (_subscriptions.TryRemove(subscriptionId, out _))
        {
            if (_unsubscribeActions.TryRemove(subscriptionId, out var unsubscribe))
            {
                unsubscribe();
            }

            _logger.LogDebug("Unsubscribed (subscription: {SubscriptionId})", subscriptionId);
        }
    }

    private void RemoveAllSubscriptions()
    {
        foreach (var id in _subscriptions.Keys.ToArray())
        {
            RemoveSubscription(id);
        }
    }

    private static OjsEvent MapEvent(OJSEvent evt)
    {
        var data = evt.Data;
        var jobId = data?.TryGetValue("job_id", out var jid) == true ? jid?.ToString() ?? "" : "";
        var jobType = data?.TryGetValue("job_type", out var jt) == true ? jt?.ToString() ?? "" : "";
        var state = data?.TryGetValue("state", out var s) == true ? s?.ToString() ?? "" : "";

        return new OjsEvent(
            evt.Type,
            jobId,
            jobType,
            state,
            evt.Time,
            data is not null ? new Dictionary<string, object?>(data) : null);
    }

    private sealed record SubscriptionEntry(string? EventType, string? JobType, Func<OjsEvent, Task> Handler);

    private sealed class Subscription : IDisposable
    {
        private readonly OjsEventSubscriptionService _service;
        private readonly Guid _subscriptionId;
        private bool _disposed;

        public Subscription(OjsEventSubscriptionService service, Guid subscriptionId)
        {
            _service = service;
            _subscriptionId = subscriptionId;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _service.RemoveSubscription(_subscriptionId);
        }
    }
}

/// <summary>
/// Represents an OJS event delivered to subscribers.
/// </summary>
/// <param name="EventType">The event type (e.g., "job.completed", "workflow.failed").</param>
/// <param name="JobId">The related job ID, if applicable.</param>
/// <param name="JobType">The related job type, if applicable.</param>
/// <param name="State">The current state of the related resource.</param>
/// <param name="Timestamp">When the event occurred.</param>
/// <param name="Metadata">Optional additional metadata from the event payload.</param>
public record OjsEvent(
    string EventType,
    string JobId,
    string JobType,
    string State,
    DateTimeOffset Timestamp,
    Dictionary<string, object?>? Metadata = null);

/// <summary>
/// Interface for typed OJS event handlers that can be registered with the DI container.
/// </summary>
public interface IOjsEventHandler
{
    /// <summary>
    /// The event type this handler processes (e.g., "job.completed").
    /// </summary>
    string EventType { get; }

    /// <summary>
    /// Handles an OJS event.
    /// </summary>
    /// <param name="evt">The event to handle.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandleAsync(OjsEvent evt, CancellationToken ct = default);
}
