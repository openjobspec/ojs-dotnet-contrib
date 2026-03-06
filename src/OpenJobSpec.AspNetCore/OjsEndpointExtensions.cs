using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenJobSpec;
using System.Text.Json;

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
        return endpoints.MapPost(pattern, async (HttpContext context) =>
        {
            var logger = context.RequestServices.GetService<ILoggerFactory>()
                ?.CreateLogger("OpenJobSpec.Webhook");

            WebhookRequest? request;
            try
            {
                request = await JsonSerializer.DeserializeAsync<WebhookRequest>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                logger?.LogWarning(ex, "Failed to deserialize OJS webhook request");
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "failed",
                    error = new { code = "invalid_request", message = "Invalid JSON payload", retryable = false },
                });
                return;
            }

            if (request?.Job == null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "failed",
                    error = new { code = "invalid_request", message = "Missing job in request body", retryable = false },
                });
                return;
            }

            var registrations = context.RequestServices.GetServices<OjsHandlerRegistration>();
            var registration = registrations.FirstOrDefault(r => r.JobType == request.Job.Type);

            if (registration == null)
            {
                logger?.LogWarning("No handler registered for job type: {JobType}", request.Job.Type);
                context.Response.StatusCode = 422;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "failed",
                    error = new
                    {
                        code = "no_handler",
                        message = $"No handler registered for job type: {request.Job.Type}",
                        retryable = false,
                    },
                });
                return;
            }

            try
            {
                using var scope = context.RequestServices.CreateScope();
                var handler = (IOjsJobHandler)scope.ServiceProvider.GetRequiredService(registration.HandlerType);

                var job = new Job
                {
                    Id = request.Job.Id,
                    Type = request.Job.Type,
                    State = Enum.TryParse<JobState>(request.Job.State, true, out var state) ? state : JobState.Active,
                    Queue = request.Job.Queue,
                    Priority = request.Job.Priority,
                    Attempt = request.Job.Attempt,
                    MaxAttempts = request.Job.MaxAttempts,
                };

                if (request.Job.Args is not null)
                {
                    job.Args = new List<object?>(request.Job.Args);
                }

                if (request.Job.Meta is not null)
                {
                    job.Meta = new Dictionary<string, object?>(request.Job.Meta);
                }

                var jobContext = new JobContext(job);
                await handler.HandleAsync(jobContext);

                logger?.LogInformation("Webhook job {JobId} ({JobType}) completed", request.Job.Id, request.Job.Type);

                await context.Response.WriteAsJsonAsync(new
                {
                    status = "completed",
                    job_id = request.Job.Id,
                });
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Webhook job {JobId} ({JobType}) failed", request.Job.Id, request.Job.Type);

                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "failed",
                    job_id = request.Job.Id,
                    error = new
                    {
                        code = "handler_error",
                        message = ex.Message,
                        retryable = true,
                    },
                });
            }
        })
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
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string State { get; set; } = "active";
    public object[]? Args { get; set; }
    public string Queue { get; set; } = "default";
    public int Priority { get; set; }
    public int Attempt { get; set; } = 1;
    public int MaxAttempts { get; set; } = 3;
    public Dictionary<string, object?>? Meta { get; set; }
}
