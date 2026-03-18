using OpenJobSpec.AspNetCore;

namespace OpenJobSpec.AspNetCore.Tests;

public class OjsCronEndpointTests
{
    [Fact]
    public void CronScheduleRequest_RecordProperties_ArePreserved()
    {
        var request = new CronScheduleRequest(
            Name: "nightly-cleanup",
            Schedule: "0 2 * * *",
            JobType: "cleanup.run",
            Args: ["database"],
            Queue: "maintenance",
            Timezone: "America/New_York");

        Assert.Equal("nightly-cleanup", request.Name);
        Assert.Equal("0 2 * * *", request.Schedule);
        Assert.Equal("cleanup.run", request.JobType);
        Assert.Single(request.Args!);
        Assert.Equal("database", request.Args![0]);
        Assert.Equal("maintenance", request.Queue);
        Assert.Equal("America/New_York", request.Timezone);
    }

    [Fact]
    public void CronScheduleRequest_DefaultValues_AreApplied()
    {
        var request = new CronScheduleRequest("daily-report", "0 9 * * *", "report.generate");

        Assert.Null(request.Args);
        Assert.Equal("default", request.Queue);
        Assert.Null(request.Timezone);
    }

    [Fact]
    public void CronScheduleResponse_RecordProperties_ArePreserved()
    {
        var nextRun = DateTimeOffset.UtcNow.AddHours(1);
        var response = new CronScheduleResponse(
            Id: "cron-001",
            Name: "nightly-cleanup",
            Schedule: "0 2 * * *",
            JobType: "cleanup.run",
            Status: "active",
            NextRunAt: nextRun);

        Assert.Equal("cron-001", response.Id);
        Assert.Equal("nightly-cleanup", response.Name);
        Assert.Equal("0 2 * * *", response.Schedule);
        Assert.Equal("cleanup.run", response.JobType);
        Assert.Equal("active", response.Status);
        Assert.Equal(nextRun, response.NextRunAt);
    }

    [Fact]
    public void CronScheduleResponse_NullNextRunAt_IsAllowed()
    {
        var response = new CronScheduleResponse("cron-002", "test", "* * * * *", "test.job", "paused", null);

        Assert.Null(response.NextRunAt);
    }

    [Fact]
    public void CronScheduleRequest_Equality_WorksByValue()
    {
        var req1 = new CronScheduleRequest("daily", "0 9 * * *", "report.generate");
        var req2 = new CronScheduleRequest("daily", "0 9 * * *", "report.generate");

        Assert.Equal(req1, req2);
    }

    [Fact]
    public void CronScheduleResponse_Equality_WorksByValue()
    {
        var nextRun = DateTimeOffset.UtcNow;
        var resp1 = new CronScheduleResponse("id", "name", "0 * * * *", "type", "active", nextRun);
        var resp2 = new CronScheduleResponse("id", "name", "0 * * * *", "type", "active", nextRun);

        Assert.Equal(resp1, resp2);
    }

    [Fact]
    public void CronScheduleRequest_WithOnlyRequired_HasCorrectDefaults()
    {
        var request = new CronScheduleRequest("test", "*/5 * * * *", "health.check");

        Assert.Equal("test", request.Name);
        Assert.Equal("*/5 * * * *", request.Schedule);
        Assert.Equal("health.check", request.JobType);
        Assert.Equal("default", request.Queue);
        Assert.Null(request.Args);
        Assert.Null(request.Timezone);
    }
}
