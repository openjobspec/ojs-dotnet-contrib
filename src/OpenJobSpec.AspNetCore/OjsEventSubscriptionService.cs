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
    private readonly SubscriptionRegistry _subscriptions = new();

    /// <summary>Creates an event subscription service.</summary>
    /// <param name="worker">Worker whose event stream is observed.</param>
    /// <param name="logger">Logger for subscription failures.</param>
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

        var subscriptionId = _subscriptions.Add(this, () => _worker.Events.On(eventType, async evt =>
        {
            var ojsEvent = MapEvent(evt);
            await handler(ojsEvent);
        }));
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

        var subscriptionId = _subscriptions.Add(this, () => _worker.Events.OnAny(async evt =>
        {
            var data = evt.Data;
            if (data is not null && data.TryGetValue("job_type", out var jt) && jt?.ToString() == jobType)
            {
                var ojsEvent = MapEvent(evt);
                await handler(ojsEvent);
            }
        }));
        _logger.LogDebug("Subscribed to job type '{JobType}' events (subscription: {SubscriptionId})", jobType, subscriptionId);

        return new Subscription(this, subscriptionId);
    }

    /// <summary>
    /// Start listening for events. The worker must already be started for events to flow.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public Task StartAsync(CancellationToken ct = default)
    {
        _subscriptions.ThrowIfDisposed(this);
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
        var subscriptionIds = _subscriptions.BeginDispose();
        if (subscriptionIds is null)
            return;

        foreach (var subscriptionId in subscriptionIds)
        {
            RemoveSubscription(subscriptionId);
        }
    }

    private void RemoveSubscription(Guid subscriptionId)
    {
        if (_subscriptions.Remove(subscriptionId))
        {
            _logger.LogDebug("Unsubscribed (subscription: {SubscriptionId})", subscriptionId);
        }
    }

    private void RemoveAllSubscriptions()
    {
        foreach (var id in _subscriptions.GetIds())
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

    private sealed class SubscriptionRegistry
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, Action> _unsubscribeActions = new();
        private bool _disposed;

        internal int Count
        {
            get
            {
                lock (_gate)
                {
                    return _unsubscribeActions.Count;
                }
            }
        }

        internal Guid Add(object owner, Func<Action> subscribe)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, owner);

                var subscriptionId = Guid.NewGuid();
                _unsubscribeActions.Add(subscriptionId, subscribe());
                return subscriptionId;
            }
        }

        internal bool Remove(Guid subscriptionId)
        {
            Action? unsubscribe;
            lock (_gate)
            {
                if (!_unsubscribeActions.Remove(subscriptionId, out unsubscribe))
                    return false;
            }

            unsubscribe();
            return true;
        }

        internal Guid[] GetIds()
        {
            lock (_gate)
            {
                return _unsubscribeActions.Keys.ToArray();
            }
        }

        internal void ThrowIfDisposed(object owner)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, owner);
            }
        }

        internal Guid[]? BeginDispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return null;

                _disposed = true;
                return _unsubscribeActions.Keys.ToArray();
            }
        }
    }

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
