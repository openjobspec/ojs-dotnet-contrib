using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenJobSpec.WorkerService;

namespace OpenJobSpec.WorkerService.Tests;

public class OjsCronSchedulerTests
{
    [Fact]
    public void OjsCronRegistration_RecordProperties()
    {
        var reg = new OjsCronRegistration("hourly", "0 * * * *", "report.generate");

        Assert.Equal("hourly", reg.Name);
        Assert.Equal("0 * * * *", reg.CronExpression);
        Assert.Equal("report.generate", reg.JobType);
        Assert.Null(reg.Args);
        Assert.Equal("default", reg.Queue);
        Assert.Null(reg.Timezone);
    }

    [Fact]
    public void OjsCronRegistration_WithAllFields()
    {
        var args = new object[] { "report-type", 42 };
        var reg = new OjsCronRegistration(
            "daily", "0 2 * * *", "report.generate",
            args, "reports", "America/New_York");

        Assert.Equal("daily", reg.Name);
        Assert.Equal("reports", reg.Queue);
        Assert.Equal("America/New_York", reg.Timezone);
        Assert.NotNull(reg.Args);
        Assert.Equal(2, reg.Args.Length);
    }

    [Fact]
    public void OjsCronOptions_Defaults()
    {
        var options = new OjsCronOptions();

        Assert.False(options.Enabled);
        Assert.Equal(60.0, options.CheckIntervalSeconds);
    }

    [Fact]
    public void CronExpressionParser_IsMatch_WildcardAll()
    {
        var time = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        Assert.True(CronExpressionParser.IsMatch("* * * * *", time));
    }

    [Fact]
    public void CronExpressionParser_IsMatch_SpecificMinute()
    {
        var time = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        Assert.True(CronExpressionParser.IsMatch("30 * * * *", time));
        Assert.False(CronExpressionParser.IsMatch("15 * * * *", time));
    }

    [Fact]
    public void CronExpressionParser_IsMatch_HourAndMinute()
    {
        var time = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        Assert.True(CronExpressionParser.IsMatch("30 14 * * *", time));
        Assert.False(CronExpressionParser.IsMatch("30 10 * * *", time));
    }

    [Fact]
    public void CronExpressionParser_IsMatch_StepExpression()
    {
        var time0 = new DateTimeOffset(2024, 6, 15, 14, 0, 0, TimeSpan.Zero);
        var time15 = new DateTimeOffset(2024, 6, 15, 14, 15, 0, TimeSpan.Zero);
        var time30 = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        var time7 = new DateTimeOffset(2024, 6, 15, 14, 7, 0, TimeSpan.Zero);

        Assert.True(CronExpressionParser.IsMatch("*/15 * * * *", time0));
        Assert.True(CronExpressionParser.IsMatch("*/15 * * * *", time15));
        Assert.True(CronExpressionParser.IsMatch("*/15 * * * *", time30));
        Assert.False(CronExpressionParser.IsMatch("*/15 * * * *", time7));
    }

    [Fact]
    public void CronExpressionParser_IsMatch_RangeExpression()
    {
        // June 15, 2024 is a Saturday (DayOfWeek = 6)
        var saturday = new DateTimeOffset(2024, 6, 15, 9, 0, 0, TimeSpan.Zero);
        // June 14, 2024 is a Friday (DayOfWeek = 5)
        var friday = new DateTimeOffset(2024, 6, 14, 9, 0, 0, TimeSpan.Zero);

        Assert.False(CronExpressionParser.IsMatch("0 9 * * 1-5", saturday));
        Assert.True(CronExpressionParser.IsMatch("0 9 * * 1-5", friday));
    }

    [Fact]
    public void CronExpressionParser_IsMatch_ListExpression()
    {
        var time = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        Assert.True(CronExpressionParser.IsMatch("0,15,30,45 * * * *", time));
        Assert.False(CronExpressionParser.IsMatch("0,15,45 * * * *", time));
    }

    [Fact]
    public void CronExpressionParser_IsMatch_DayOfMonth()
    {
        var time = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero);
        Assert.True(CronExpressionParser.IsMatch("0 0 15 * *", time));
        Assert.False(CronExpressionParser.IsMatch("0 0 1 * *", time));
    }

    [Fact]
    public void CronExpressionParser_IsMatch_Month()
    {
        var june = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.True(CronExpressionParser.IsMatch("0 0 1 6 *", june));
        Assert.False(CronExpressionParser.IsMatch("0 0 1 12 *", june));
    }

    [Fact]
    public void CronExpressionParser_IsMatch_InvalidExpression_Throws()
    {
        var time = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => CronExpressionParser.IsMatch("* * *", time));
    }

    [Fact]
    public void CronExpressionParser_GetNextOccurrence_FindsNext()
    {
        var from = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        var next = CronExpressionParser.GetNextOccurrence("0 * * * *", from);

        Assert.NotNull(next);
        Assert.Equal(0, next.Value.Minute);
        Assert.Equal(15, next.Value.Hour);
    }

    [Fact]
    public void CronExpressionParser_GetNextOccurrence_SkipsCurrent()
    {
        // Exactly on the mark — should find the NEXT occurrence
        var from = new DateTimeOffset(2024, 6, 15, 14, 0, 0, TimeSpan.Zero);
        var next = CronExpressionParser.GetNextOccurrence("0 * * * *", from);

        Assert.NotNull(next);
        Assert.Equal(15, next.Value.Hour);
        Assert.Equal(0, next.Value.Minute);
    }

    [Fact]
    public void CronExpressionParser_GetNextOccurrence_EveryFiveMinutes()
    {
        var from = new DateTimeOffset(2024, 6, 15, 14, 12, 0, TimeSpan.Zero);
        var next = CronExpressionParser.GetNextOccurrence("*/5 * * * *", from);

        Assert.NotNull(next);
        Assert.Equal(15, next.Value.Minute);
        Assert.Equal(14, next.Value.Hour);
    }

    [Fact]
    public void AddOjsCronSchedule_RegistersSchedule()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts => opts.BaseUrl = "http://test:8080");
        builder.Services.AddOjsCronSchedule("hourly", "0 * * * *", "report.generate");

        var provider = builder.Services.BuildServiceProvider();
        var schedules = provider.GetServices<OjsCronRegistration>().ToList();

        Assert.Single(schedules);
        Assert.Equal("hourly", schedules[0].Name);
        Assert.Equal("0 * * * *", schedules[0].CronExpression);
        Assert.Equal("report.generate", schedules[0].JobType);
    }

    [Fact]
    public void CronExpressionParser_IsMatch_WildcardAllFields()
    {
        // Ensure wildcard matches at different times
        var midnight = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var noon = new DateTimeOffset(2024, 7, 15, 12, 30, 0, TimeSpan.Zero);
        var evening = new DateTimeOffset(2024, 12, 31, 23, 59, 0, TimeSpan.Zero);

        Assert.True(CronExpressionParser.IsMatch("* * * * *", midnight));
        Assert.True(CronExpressionParser.IsMatch("* * * * *", noon));
        Assert.True(CronExpressionParser.IsMatch("* * * * *", evening));
    }
}
