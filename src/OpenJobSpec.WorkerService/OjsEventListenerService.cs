using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenJobSpec;

namespace OpenJobSpec.WorkerService;

/// <summary>
/// Background service that listens for OJS events and dispatches to registered handlers.
/// Runs alongside the worker as a separate background service.
/// </summary>
internal sealed class OjsEventListenerService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly OjsEventListenerOptions _options;
    private readonly ILogger<OjsEventListenerService> _logger;
    private readonly HashSet<string> _eventTypeFilter;
    private readonly TaskCompletionSource _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public OjsEventListenerService(
        IServiceProvider services,
        OjsEventListenerOptions options,
        ILogger<OjsEventListenerService> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
        _eventTypeFilter = new HashSet<string>(options.EventTypes);
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken);
        await _ready.Task.WaitAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OJS Event Listener starting (types: [{Types}], poll interval: {Interval}s)",
            string.Join(", ", _options.EventTypes),
            _options.PollIntervalSeconds);

        try
        {
            var worker = _services.GetService<OJSWorker>();
            if (worker is not null)
            {
                worker.Events.OnAny(async evt =>
                {
                    if (_eventTypeFilter.Contains(evt.Type))
                    {
                        var eventData = MapFromWorkerEvent(evt);
                        await OjsEventListenerDispatcher.DispatchAsync(
                            _services,
                            _logger,
                            eventData,
                            stoppingToken);
                    }
                });

                _logger.LogInformation("Subscribed to OJS worker events");
            }
            else
            {
                _logger.LogWarning("OJS Worker not available, event listener running in poll-only mode");
            }

            _ready.TrySetResult();
        }
        catch (Exception ex)
        {
            _ready.TrySetException(ex);
            throw;
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }

        _logger.LogInformation("OJS Event Listener stopped");
    }

    private static OjsEventData MapFromWorkerEvent(OJSEvent evt)
    {
        var data = evt.Data;
        return new OjsEventData(
            EventType: evt.Type,
            JobId: evt.Subject ?? string.Empty,
            JobType: data?.TryGetValue("type", out var jt) == true ? jt?.ToString() ?? string.Empty : string.Empty,
            State: data?.TryGetValue("state", out var st) == true ? st?.ToString() ?? string.Empty : string.Empty,
            Timestamp: evt.Time,
            Attempt: data?.TryGetValue("attempt", out var att) == true && att is int a ? a : 0,
            Queue: data?.TryGetValue("queue", out var q) == true ? q?.ToString() ?? "default" : "default",
            Metadata: data
        );
    }
}

internal static class OjsEventListenerDispatcher
{
    internal static async Task DispatchAsync(
        IServiceProvider services,
        ILogger logger,
        OjsEventData eventData,
        CancellationToken ct)
    {
        try
        {
            using var scope = services.CreateScope();
            var listeners = scope.ServiceProvider
                .GetServices<IOjsEventListener>()
                .Where(listener => listener.EventType == eventData.EventType);

            foreach (var listener in listeners)
            {
                await DispatchToListenerAsync(listener, logger, eventData, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to dispatch event {EventType}", eventData.EventType);
        }
    }

    private static async Task DispatchToListenerAsync(
        IOjsEventListener listener,
        ILogger logger,
        OjsEventData eventData,
        CancellationToken ct)
    {
        try
        {
            await listener.HandleAsync(eventData, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Event listener {Listener} failed for event {EventType} (job: {JobId})",
                listener.GetType().Name, eventData.EventType, eventData.JobId);
        }
    }
}

/// <summary>
/// Interface for typed event listeners. Implement to handle specific OJS event types.
/// </summary>
public interface IOjsEventListener
{
    /// <summary>Event type this listener handles (e.g., "job.completed", "job.failed").</summary>
    string EventType { get; }

    /// <summary>Handle the event.</summary>
    Task HandleAsync(OjsEventData eventData, CancellationToken ct = default);
}

/// <summary>
/// Event data received from the OJS backend.
/// </summary>
public record OjsEventData(
    string EventType,
    string JobId,
    string JobType,
    string State,
    DateTimeOffset Timestamp,
    int Attempt,
    string Queue,
    Dictionary<string, object?>? Metadata = null
);

/// <summary>
/// Options for the event listener service.
/// </summary>
public class OjsEventListenerOptions
{
    /// <summary>Whether the event listener service is enabled.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Seconds between poll cycles for server-side events.</summary>
    public double PollIntervalSeconds { get; set; } = 5.0;

    /// <summary>Event types to listen for.</summary>
    public string[] EventTypes { get; set; } = ["job.completed", "job.failed", "job.retrying"];
}
