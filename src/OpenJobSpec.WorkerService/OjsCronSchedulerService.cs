using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenJobSpec;

namespace OpenJobSpec.WorkerService;

/// <summary>
/// Background service that manages cron-scheduled jobs.
/// Periodically checks registered cron expressions and enqueues jobs when due.
/// </summary>
internal sealed class OjsCronSchedulerService : BackgroundService
{
    private readonly OJSClient _client;
    private readonly IEnumerable<OjsCronRegistration> _schedules;
    private readonly OjsCronOptions _options;
    private readonly ILogger<OjsCronSchedulerService> _logger;
    private readonly Dictionary<string, DateTimeOffset> _lastRuns = new();

    public OjsCronSchedulerService(
        OJSClient client,
        IEnumerable<OjsCronRegistration> schedules,
        OjsCronOptions options,
        ILogger<OjsCronSchedulerService> logger)
    {
        _client = client;
        _schedules = schedules;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var scheduleList = _schedules.ToList();
        _logger.LogInformation(
            "OJS Cron Scheduler starting ({Count} schedules, check interval: {Interval}s)",
            scheduleList.Count, _options.CheckIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var schedule in scheduleList)
            {
                try
                {
                    var checkTime = now;
                    if (schedule.Timezone is not null)
                    {
                        var tz = TimeZoneInfo.FindSystemTimeZoneById(schedule.Timezone);
                        checkTime = TimeZoneInfo.ConvertTime(now, tz);
                    }

                    if (!CronExpressionParser.IsMatch(schedule.CronExpression, checkTime))
                        continue;

                    // Prevent duplicate triggers within the same minute
                    if (_lastRuns.TryGetValue(schedule.Name, out var lastRun) &&
                        (now - lastRun).TotalSeconds < 60)
                        continue;

                    var args = schedule.Args is not null
                        ? new List<object?>(schedule.Args)
                        : null;

                    await _client.EnqueueAsync(
                        schedule.JobType,
                        args,
                        new EnqueueOptions { Queue = schedule.Queue });

                    _lastRuns[schedule.Name] = now;
                    _logger.LogInformation(
                        "Cron schedule '{Name}' triggered, enqueued job type '{JobType}' to queue '{Queue}'",
                        schedule.Name, schedule.JobType, schedule.Queue);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process cron schedule '{Name}'", schedule.Name);
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CheckIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("OJS Cron Scheduler stopped");
    }
}

/// <summary>
/// Registration record for a cron schedule.
/// </summary>
public record OjsCronRegistration(
    string Name,
    string CronExpression,
    string JobType,
    object[]? Args = null,
    string Queue = "default",
    string? Timezone = null
);

/// <summary>
/// Options for the cron scheduler.
/// </summary>
public class OjsCronOptions
{
    /// <summary>Whether the cron scheduler service is enabled.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Seconds between cron expression checks.</summary>
    public double CheckIntervalSeconds { get; set; } = 60.0;
}

/// <summary>
/// Cron expression parser for basic cron syntax (minute hour day month weekday).
/// Supports wildcards (*), specific values, ranges (1-5), lists (1,3,5), and steps (*/5).
/// </summary>
public static class CronExpressionParser
{
    /// <summary>
    /// Checks if a cron expression matches the given time.
    /// </summary>
    /// <param name="cronExpression">Standard 5-field cron expression (minute hour day month weekday).</param>
    /// <param name="time">The time to check against.</param>
    /// <returns>True if the expression matches the time.</returns>
    public static bool IsMatch(string cronExpression, DateTimeOffset time)
    {
        var fields = cronExpression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
            throw new ArgumentException(
                $"Invalid cron expression: expected 5 fields, got {fields.Length}", nameof(cronExpression));

        return FieldMatches(fields[0], time.Minute, 0, 59) &&
               FieldMatches(fields[1], time.Hour, 0, 23) &&
               FieldMatches(fields[2], time.Day, 1, 31) &&
               FieldMatches(fields[3], time.Month, 1, 12) &&
               FieldMatches(fields[4], (int)time.DayOfWeek, 0, 6);
    }

    /// <summary>
    /// Gets the next occurrence after the given time that matches the cron expression.
    /// Returns null if no occurrence is found within 366 days.
    /// </summary>
    /// <param name="cronExpression">Standard 5-field cron expression.</param>
    /// <param name="from">The starting time to search from (exclusive).</param>
    /// <returns>The next matching time, or null if none found within 366 days.</returns>
    public static DateTimeOffset? GetNextOccurrence(string cronExpression, DateTimeOffset from)
    {
        var next = new DateTimeOffset(
            from.Year, from.Month, from.Day,
            from.Hour, from.Minute, 0, from.Offset).AddMinutes(1);

        var limit = from.AddDays(366);

        while (next <= limit)
        {
            if (IsMatch(cronExpression, next))
                return next;

            next = next.AddMinutes(1);
        }

        return null;
    }

    private static bool FieldMatches(string field, int value, int min, int max)
    {
        if (field == "*")
            return true;

        foreach (var part in field.Split(','))
        {
            if (part.Contains('/'))
            {
                var stepParts = part.Split('/', 2);
                var step = int.Parse(stepParts[1]);
                var rangeStart = stepParts[0] == "*" ? min : int.Parse(stepParts[0]);

                for (var i = rangeStart; i <= max; i += step)
                {
                    if (i == value) return true;
                }
            }
            else if (part.Contains('-'))
            {
                var rangeParts = part.Split('-', 2);
                var rangeFrom = int.Parse(rangeParts[0]);
                var rangeTo = int.Parse(rangeParts[1]);

                if (value >= rangeFrom && value <= rangeTo) return true;
            }
            else
            {
                if (int.Parse(part) == value) return true;
            }
        }

        return false;
    }
}
