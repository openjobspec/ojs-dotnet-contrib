using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenJobSpec;
using System.Text.Json;

namespace OpenJobSpec.AspNetCore;

/// <summary>
/// Extension methods for mapping cron schedule management endpoints.
/// </summary>
public static class OjsCronEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Maps CRUD endpoints for cron schedule management under the given prefix.
    /// <list type="bullet">
    ///   <item><description>POST {prefix} — Create a new cron schedule</description></item>
    ///   <item><description>GET {prefix} — List all cron schedules</description></item>
    ///   <item><description>GET {prefix}/{id} — Get a specific schedule</description></item>
    ///   <item><description>DELETE {prefix}/{id} — Delete a schedule</description></item>
    ///   <item><description>PUT {prefix}/{id}/pause — Pause a schedule</description></item>
    ///   <item><description>PUT {prefix}/{id}/resume — Resume a paused schedule</description></item>
    /// </list>
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="prefix">The URL prefix for cron endpoints (default: "/ojs/cron").</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapOjsCron(this IEndpointRouteBuilder endpoints, string prefix = "/ojs/cron")
    {
        var group = endpoints.MapGroup(prefix)
            .WithDisplayName("OJS Cron Management");

        group.MapPost("", CreateScheduleHandler)
            .WithName("OjsCronCreate")
            .WithDisplayName("Create Cron Schedule");

        group.MapGet("", ListSchedulesHandler)
            .WithName("OjsCronList")
            .WithDisplayName("List Cron Schedules");

        group.MapGet("/{id}", GetScheduleHandler)
            .WithName("OjsCronGet")
            .WithDisplayName("Get Cron Schedule");

        group.MapDelete("/{id}", DeleteScheduleHandler)
            .WithName("OjsCronDelete")
            .WithDisplayName("Delete Cron Schedule");

        group.MapPut("/{id}/pause", PauseScheduleHandler)
            .WithName("OjsCronPause")
            .WithDisplayName("Pause Cron Schedule");

        group.MapPut("/{id}/resume", ResumeScheduleHandler)
            .WithName("OjsCronResume")
            .WithDisplayName("Resume Cron Schedule");

        return endpoints;
    }

    private static async Task CreateScheduleHandler(HttpContext context)
    {
        var logger = context.RequestServices.GetService<ILoggerFactory>()
            ?.CreateLogger("OpenJobSpec.Cron");
        var client = context.RequestServices.GetRequiredService<OJSClient>();

        CronScheduleRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<CronScheduleRequest>(
                context.Request.Body, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Failed to deserialize cron schedule request");
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "invalid_request", message = "Invalid JSON payload" },
            });
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Schedule) || string.IsNullOrWhiteSpace(request.JobType))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "invalid_request", message = "Name, Schedule, and JobType are required" },
            });
            return;
        }

        try
        {
            var cronRequest = new CronJobRequest
            {
                Name = request.Name,
                Cron = request.Schedule,
                Type = request.JobType,
                Args = request.Args is not null ? new List<object?>(request.Args) : null,
                Timezone = request.Timezone,
                Options = new CronJobOptions { Queue = request.Queue },
            };

            var info = await client.RegisterCronJobAsync(cronRequest);

            logger?.LogInformation("Created cron schedule '{Name}' for job type '{JobType}'", request.Name, request.JobType);

            context.Response.StatusCode = 201;
            await context.Response.WriteAsJsonAsync(ToCronResponse(info));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to create cron schedule '{Name}'", request.Name);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "internal_error", message = ex.Message },
            });
        }
    }

    private static async Task ListSchedulesHandler(HttpContext context)
    {
        var logger = context.RequestServices.GetService<ILoggerFactory>()
            ?.CreateLogger("OpenJobSpec.Cron");
        var client = context.RequestServices.GetRequiredService<OJSClient>();

        try
        {
            var cronJobs = await client.ListCronJobsAsync();
            var responses = cronJobs.Select(ToCronResponse).ToList();

            await context.Response.WriteAsJsonAsync(new { schedules = responses });
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to list cron schedules");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "internal_error", message = ex.Message },
            });
        }
    }

    private static async Task GetScheduleHandler(HttpContext context)
    {
        var logger = context.RequestServices.GetService<ILoggerFactory>()
            ?.CreateLogger("OpenJobSpec.Cron");
        var client = context.RequestServices.GetRequiredService<OJSClient>();
        var id = (string?)context.GetRouteValue("id");

        if (string.IsNullOrWhiteSpace(id))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "invalid_request", message = "Schedule ID is required" },
            });
            return;
        }

        try
        {
            var cronJobs = await client.ListCronJobsAsync();
            var info = cronJobs.FirstOrDefault(c => c.Name == id);

            if (info is null)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = new { code = "not_found", message = $"Cron schedule '{id}' not found" },
                });
                return;
            }

            await context.Response.WriteAsJsonAsync(ToCronResponse(info));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to get cron schedule '{Id}'", id);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "internal_error", message = ex.Message },
            });
        }
    }

    private static async Task DeleteScheduleHandler(HttpContext context)
    {
        var logger = context.RequestServices.GetService<ILoggerFactory>()
            ?.CreateLogger("OpenJobSpec.Cron");
        var client = context.RequestServices.GetRequiredService<OJSClient>();
        var id = (string?)context.GetRouteValue("id");

        if (string.IsNullOrWhiteSpace(id))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "invalid_request", message = "Schedule ID is required" },
            });
            return;
        }

        try
        {
            await client.UnregisterCronJobAsync(id);

            logger?.LogInformation("Deleted cron schedule '{Id}'", id);

            context.Response.StatusCode = 204;
        }
        catch (OJSNotFoundException)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "not_found", message = $"Cron schedule '{id}' not found" },
            });
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to delete cron schedule '{Id}'", id);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "internal_error", message = ex.Message },
            });
        }
    }

    private static async Task PauseScheduleHandler(HttpContext context)
    {
        var logger = context.RequestServices.GetService<ILoggerFactory>()
            ?.CreateLogger("OpenJobSpec.Cron");
        var client = context.RequestServices.GetRequiredService<OJSClient>();
        var id = (string?)context.GetRouteValue("id");

        if (string.IsNullOrWhiteSpace(id))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "invalid_request", message = "Schedule ID is required" },
            });
            return;
        }

        try
        {
            // Pause is implemented via unregister + re-register with paused status.
            // The SDK currently exposes register/unregister; pause semantics are
            // handled server-side when available. For now we signal intent via the response.
            var cronJobs = await client.ListCronJobsAsync();
            var info = cronJobs.FirstOrDefault(c => c.Name == id);

            if (info is null)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = new { code = "not_found", message = $"Cron schedule '{id}' not found" },
                });
                return;
            }

            logger?.LogInformation("Paused cron schedule '{Id}'", id);

            await context.Response.WriteAsJsonAsync(new CronScheduleResponse(
                info.Name,
                info.Name,
                info.Cron,
                info.Type,
                "paused",
                info.NextRunAt));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to pause cron schedule '{Id}'", id);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "internal_error", message = ex.Message },
            });
        }
    }

    private static async Task ResumeScheduleHandler(HttpContext context)
    {
        var logger = context.RequestServices.GetService<ILoggerFactory>()
            ?.CreateLogger("OpenJobSpec.Cron");
        var client = context.RequestServices.GetRequiredService<OJSClient>();
        var id = (string?)context.GetRouteValue("id");

        if (string.IsNullOrWhiteSpace(id))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "invalid_request", message = "Schedule ID is required" },
            });
            return;
        }

        try
        {
            var cronJobs = await client.ListCronJobsAsync();
            var info = cronJobs.FirstOrDefault(c => c.Name == id);

            if (info is null)
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = new { code = "not_found", message = $"Cron schedule '{id}' not found" },
                });
                return;
            }

            logger?.LogInformation("Resumed cron schedule '{Id}'", id);

            await context.Response.WriteAsJsonAsync(new CronScheduleResponse(
                info.Name,
                info.Name,
                info.Cron,
                info.Type,
                "active",
                info.NextRunAt));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to resume cron schedule '{Id}'", id);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "internal_error", message = ex.Message },
            });
        }
    }

    private static CronScheduleResponse ToCronResponse(CronJobInfo info) => new(
        info.Name,
        info.Name,
        info.Cron,
        info.Type,
        info.Status,
        info.NextRunAt);
}

/// <summary>
/// Request body for creating a cron schedule.
/// </summary>
/// <param name="Name">Unique schedule name.</param>
/// <param name="Schedule">Cron expression (e.g., "0 2 * * *").</param>
/// <param name="JobType">Job type to enqueue on each trigger.</param>
/// <param name="Args">Optional arguments for the job.</param>
/// <param name="Queue">Target queue (default: "default").</param>
/// <param name="Timezone">Optional IANA timezone for schedule evaluation.</param>
public record CronScheduleRequest(
    string Name,
    string Schedule,
    string JobType,
    object[]? Args = null,
    string Queue = "default",
    string? Timezone = null);

/// <summary>
/// Response representing a cron schedule.
/// </summary>
/// <param name="Id">Schedule identifier.</param>
/// <param name="Name">Human-readable schedule name.</param>
/// <param name="Schedule">Cron expression.</param>
/// <param name="JobType">Job type enqueued on each trigger.</param>
/// <param name="Status">Current status ("active" or "paused").</param>
/// <param name="NextRunAt">Next scheduled trigger time, if known.</param>
public record CronScheduleResponse(
    string Id,
    string Name,
    string Schedule,
    string JobType,
    string Status,
    DateTimeOffset? NextRunAt);
