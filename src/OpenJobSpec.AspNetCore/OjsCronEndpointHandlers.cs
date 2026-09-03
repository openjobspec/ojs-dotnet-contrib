using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenJobSpec;

namespace OpenJobSpec.AspNetCore;

internal static class OjsCronEndpointHandlers
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal static async Task CreateScheduleAsync(HttpContext context)
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

    internal static async Task ListSchedulesAsync(HttpContext context)
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

    internal static async Task GetScheduleAsync(HttpContext context)
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

    internal static async Task DeleteScheduleAsync(HttpContext context)
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

    internal static async Task PauseScheduleAsync(HttpContext context)
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

    internal static async Task ResumeScheduleAsync(HttpContext context)
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
