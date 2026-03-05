namespace OpenJobSpec.AspNetCore;

/// <summary>
/// Configuration options for the OJS ASP.NET Core integration.
/// </summary>
public class OjsOptions
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
    /// HTTP request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Worker-specific configuration.
    /// </summary>
    public OjsWorkerOptions Worker { get; set; } = new();
}

/// <summary>
/// Worker-specific configuration options.
/// </summary>
public class OjsWorkerOptions
{
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
}
