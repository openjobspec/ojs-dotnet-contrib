using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenJobSpec;

namespace OpenJobSpec.AspNetCore;

internal static class OjsWebhookEndpointHandler
{
    internal static async Task HandleAsync(HttpContext context)
    {
        var logger = context.RequestServices.GetService<ILoggerFactory>()
            ?.CreateLogger("OpenJobSpec.Webhook");

        WebhookRequest? request;
        try
        {
            request = await OjsWebhookRequestParser.ParseAsync(context.Request.Body);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Failed to deserialize OJS webhook request");
            await OjsWebhookResponsePolicy.WriteInvalidJsonAsync(context);
            return;
        }

        if (request?.Job == null)
        {
            await OjsWebhookResponsePolicy.WriteMissingJobAsync(context);
            return;
        }

        var registrations = context.RequestServices.GetServices<OjsHandlerRegistration>();
        var registration = registrations.FirstOrDefault(r => r.JobType == request.Job.Type);

        if (registration == null)
        {
            logger?.LogWarning("No handler registered for job type: {JobType}", request.Job.Type);
            await OjsWebhookResponsePolicy.WriteMissingHandlerAsync(context, request.Job.Type);
            return;
        }

        try
        {
            await OjsWebhookHandlerDispatcher.DispatchAsync(
                context.RequestServices,
                registration,
                request.Job);

            logger?.LogInformation("Webhook job {JobId} ({JobType}) completed", request.Job.Id, request.Job.Type);
            await OjsWebhookResponsePolicy.WriteCompletedAsync(context, request.Job.Id);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Webhook job {JobId} ({JobType}) failed", request.Job.Id, request.Job.Type);
            await OjsWebhookResponsePolicy.WriteHandlerErrorAsync(context, request.Job.Id, ex.Message);
        }
    }
}

internal static class OjsWebhookRequestParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static ValueTask<WebhookRequest?> ParseAsync(Stream body)
    {
        return JsonSerializer.DeserializeAsync<WebhookRequest>(body, SerializerOptions);
    }
}

internal static class OjsWebhookJobMapper
{
    internal static Job Map(WebhookJob request)
    {
        var job = new Job
        {
            Id = request.Id,
            Type = request.Type,
            State = Enum.TryParse<JobState>(request.State, true, out var state) ? state : JobState.Active,
            Queue = request.Queue,
            Priority = request.Priority,
            Attempt = request.Attempt,
            MaxAttempts = request.MaxAttempts,
        };

        if (request.Args is not null)
        {
            job.Args = new List<object?>(request.Args);
        }

        if (request.Meta is not null)
        {
            job.Meta = new Dictionary<string, object?>(request.Meta);
        }

        return job;
    }
}

internal static class OjsWebhookHandlerDispatcher
{
    internal static async Task DispatchAsync(
        IServiceProvider requestServices,
        OjsHandlerRegistration registration,
        WebhookJob request)
    {
        using var scope = requestServices.CreateScope();
        var handler = (IOjsJobHandler)scope.ServiceProvider.GetRequiredService(registration.HandlerType);
        var jobContext = new JobContext(OjsWebhookJobMapper.Map(request));

        await handler.HandleAsync(jobContext);
    }
}

internal static class OjsWebhookResponsePolicy
{
    internal static async Task WriteInvalidJsonAsync(HttpContext context)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new
        {
            status = "failed",
            error = new { code = "invalid_request", message = "Invalid JSON payload", retryable = false },
        });
    }

    internal static async Task WriteMissingJobAsync(HttpContext context)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new
        {
            status = "failed",
            error = new { code = "invalid_request", message = "Missing job in request body", retryable = false },
        });
    }

    internal static async Task WriteMissingHandlerAsync(HttpContext context, string jobType)
    {
        context.Response.StatusCode = 422;
        await context.Response.WriteAsJsonAsync(new
        {
            status = "failed",
            error = new
            {
                code = "no_handler",
                message = $"No handler registered for job type: {jobType}",
                retryable = false,
            },
        });
    }

    internal static Task WriteCompletedAsync(HttpContext context, string jobId)
    {
        return context.Response.WriteAsJsonAsync(new
        {
            status = "completed",
            job_id = jobId,
        });
    }

    internal static async Task WriteHandlerErrorAsync(HttpContext context, string jobId, string message)
    {
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new
        {
            status = "failed",
            job_id = jobId,
            error = new
            {
                code = "handler_error",
                message,
                retryable = true,
            },
        });
    }
}
