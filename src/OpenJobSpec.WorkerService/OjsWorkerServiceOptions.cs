namespace OpenJobSpec.WorkerService;

/// <summary>
/// Configuration options for the OJS Worker Service integration.
/// Designed for standalone background processing without ASP.NET Core.
/// </summary>
public class OjsWorkerServiceOptions
{
    /// <summary>
    /// Base URL of the OJS backend server.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>
    /// Optional Bearer token for authentication.
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>
    /// Queues the worker will consume from.
    /// </summary>
    public string[] Queues { get; set; } = ["default"];

    /// <summary>
    /// Number of concurrent job processors.
    /// </summary>
    public int Concurrency { get; set; } = 10;

    /// <summary>
    /// Seconds between poll cycles.
    /// </summary>
    public double PollIntervalSeconds { get; set; } = 2.0;

    /// <summary>
    /// Seconds between heartbeats.
    /// </summary>
    public double HeartbeatIntervalSeconds { get; set; } = 5.0;

    /// <summary>
    /// Seconds to wait for in-flight jobs on shutdown.
    /// </summary>
    public double ShutdownTimeoutSeconds { get; set; } = 25.0;

    /// <summary>
    /// Whether to register a health check for the OJS backend.
    /// </summary>
    public bool EnableHealthCheck { get; set; } = true;

    /// <summary>
    /// Name of the health check (when enabled).
    /// </summary>
    public string HealthCheckName { get; set; } = "ojs";

    /// <summary>
    /// Event listener configuration.
    /// </summary>
    public OjsEventListenerOptions EventListener { get; set; } = new();

    /// <summary>
    /// Cron scheduler configuration.
    /// </summary>
    public OjsCronOptions Cron { get; set; } = new();

    /// <summary>
    /// Encryption service configuration.
    /// </summary>
    public OjsEncryptionServiceOptions Encryption { get; set; } = new();

    /// <summary>
    /// Pre-shutdown hooks called before the worker stops processing.
    /// Each hook receives a cancellation token and should complete promptly.
    /// </summary>
    public List<Func<CancellationToken, Task>> PreShutdownHooks { get; set; } = [];
}
