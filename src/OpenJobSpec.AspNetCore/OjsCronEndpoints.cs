using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace OpenJobSpec.AspNetCore;

/// <summary>
/// Extension methods for mapping cron schedule management endpoints.
/// </summary>
public static class OjsCronEndpoints
{
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

        group.MapPost("", OjsCronEndpointHandlers.CreateScheduleAsync)
            .WithName("OjsCronCreate")
            .WithDisplayName("Create Cron Schedule");

        group.MapGet("", OjsCronEndpointHandlers.ListSchedulesAsync)
            .WithName("OjsCronList")
            .WithDisplayName("List Cron Schedules");

        group.MapGet("/{id}", OjsCronEndpointHandlers.GetScheduleAsync)
            .WithName("OjsCronGet")
            .WithDisplayName("Get Cron Schedule");

        group.MapDelete("/{id}", OjsCronEndpointHandlers.DeleteScheduleAsync)
            .WithName("OjsCronDelete")
            .WithDisplayName("Delete Cron Schedule");

        group.MapPut("/{id}/pause", OjsCronEndpointHandlers.PauseScheduleAsync)
            .WithName("OjsCronPause")
            .WithDisplayName("Pause Cron Schedule");

        group.MapPut("/{id}/resume", OjsCronEndpointHandlers.ResumeScheduleAsync)
            .WithName("OjsCronResume")
            .WithDisplayName("Resume Cron Schedule");

        return endpoints;
    }
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
