using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace OpenJobSpec.AspNetCore;

/// <summary>
/// Extension methods for mapping OJS endpoints in ASP.NET Core minimal APIs.
/// </summary>
public static class OjsEndpointExtensions
{
    /// <summary>
    /// Maps an OJS webhook endpoint for receiving push-delivered jobs from an OJS backend.
    /// The endpoint accepts POST requests with a job payload and dispatches to registered handlers.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The URL pattern for the webhook (default: "/ojs/webhook").</param>
    /// <returns>The route handler builder for further configuration.</returns>
    /// <example>
    /// <code>
    /// app.MapOjsWebhook("/ojs/webhook");
    /// </code>
    /// </example>
    public static IEndpointConventionBuilder MapOjsWebhook(this IEndpointRouteBuilder endpoints, string pattern = "/ojs/webhook")
    {
        return endpoints.MapPost(pattern, OjsWebhookEndpointHandler.HandleAsync)
        .WithName("OjsWebhook")
        .WithDisplayName("OJS Job Webhook");
    }
}

/// <summary>
/// Request body for OJS push-delivery webhooks.
/// </summary>
public sealed class WebhookRequest
{
    /// <summary>The job to process.</summary>
    public WebhookJob? Job { get; set; }

    /// <summary>Optional delivery ID for idempotency.</summary>
    public string? DeliveryId { get; set; }

    /// <summary>Optional worker ID assigned by the backend.</summary>
    public string? WorkerId { get; set; }
}

/// <summary>
/// Minimal job representation received in webhook payloads.
/// </summary>
public sealed class WebhookJob
{
    /// <summary>Job identifier.</summary>
    public string Id { get; set; } = "";

    /// <summary>Job type used for handler routing.</summary>
    public string Type { get; set; } = "";

    /// <summary>Current job state.</summary>
    public string State { get; set; } = "active";

    /// <summary>Job arguments.</summary>
    public object[]? Args { get; set; }

    /// <summary>Queue containing the job.</summary>
    public string Queue { get; set; } = "default";

    /// <summary>Job priority.</summary>
    public int Priority { get; set; }

    /// <summary>Current execution attempt.</summary>
    public int Attempt { get; set; } = 1;

    /// <summary>Maximum execution attempts.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Optional job metadata.</summary>
    public Dictionary<string, object?>? Meta { get; set; }
}
