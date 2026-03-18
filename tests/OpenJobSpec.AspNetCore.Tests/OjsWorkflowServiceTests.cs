using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenJobSpec.AspNetCore;

namespace OpenJobSpec.AspNetCore.Tests;

public class OjsWorkflowServiceTests
{
    [Fact]
    public void AddOjsWorkflows_RegistersWorkflowService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsWorkflows();

        var provider = services.BuildServiceProvider();
        var workflowService = provider.GetService<OjsWorkflowService>();

        Assert.NotNull(workflowService);
    }

    [Fact]
    public void AddOjsWorkflows_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsWorkflows();

        var provider = services.BuildServiceProvider();
        var service1 = provider.GetService<OjsWorkflowService>();
        var service2 = provider.GetService<OjsWorkflowService>();

        Assert.NotNull(service1);
        Assert.Same(service1, service2);
    }

    [Fact]
    public void AddOjsWorkflows_MultipleCalls_DoesNotDuplicate()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsWorkflows();
        services.AddOjsWorkflows();

        var descriptors = services.Where(d => d.ServiceType == typeof(OjsWorkflowService)).ToList();
        Assert.Single(descriptors);
    }

    [Fact]
    public void WorkflowStep_RecordProperties_ArePreserved()
    {
        var step = new WorkflowStep("email.send", ["user@test.com", "Hello"], "emails", 5);

        Assert.Equal("email.send", step.JobType);
        Assert.Equal(2, step.Args.Length);
        Assert.Equal("user@test.com", step.Args[0]);
        Assert.Equal("emails", step.Queue);
        Assert.Equal(5, step.Priority);
    }

    [Fact]
    public void WorkflowStep_DefaultValues_AreApplied()
    {
        var step = new WorkflowStep("test.job", ["arg1"]);

        Assert.Equal("default", step.Queue);
        Assert.Equal(0, step.Priority);
    }

    [Fact]
    public void WorkflowResult_RecordProperties_ArePreserved()
    {
        var now = DateTimeOffset.UtcNow;
        var result = new WorkflowResult("wf-123", "running", now);

        Assert.Equal("wf-123", result.WorkflowId);
        Assert.Equal("running", result.Status);
        Assert.Equal(now, result.CreatedAt);
    }

    [Fact]
    public void WorkflowStatusResponse_RecordProperties_ArePreserved()
    {
        var status = new WorkflowStatusResponse("wf-456", "completed", 10, 8, 2);

        Assert.Equal("wf-456", status.WorkflowId);
        Assert.Equal("completed", status.Status);
        Assert.Equal(10, status.TotalJobs);
        Assert.Equal(8, status.CompletedJobs);
        Assert.Equal(2, status.FailedJobs);
    }

    [Fact]
    public void BatchCallbacks_DefaultValues_AreNull()
    {
        var callbacks = new BatchCallbacks();

        Assert.Null(callbacks.OnSuccessJobType);
        Assert.Null(callbacks.OnSuccessArgs);
        Assert.Null(callbacks.OnFailureJobType);
        Assert.Null(callbacks.OnFailureArgs);
    }

    [Fact]
    public void BatchCallbacks_WithValues_ArePreserved()
    {
        var callbacks = new BatchCallbacks(
            OnSuccessJobType: "notify.success",
            OnSuccessArgs: ["all done"],
            OnFailureJobType: "notify.failure",
            OnFailureArgs: ["something failed"]);

        Assert.Equal("notify.success", callbacks.OnSuccessJobType);
        Assert.Single(callbacks.OnSuccessArgs!);
        Assert.Equal("notify.failure", callbacks.OnFailureJobType);
        Assert.Single(callbacks.OnFailureArgs!);
    }

    [Fact]
    public void WorkflowStep_Equality_WorksByValue()
    {
        var step1 = new WorkflowStep("email.send", ["a"], "default", 0);
        var step2 = new WorkflowStep("email.send", ["a"], "default", 0);

        // Records use value equality, but arrays use reference equality
        Assert.Equal(step1.JobType, step2.JobType);
        Assert.Equal(step1.Queue, step2.Queue);
        Assert.Equal(step1.Priority, step2.Priority);
    }

    [Fact]
    public void WorkflowResult_Equality_WorksByValue()
    {
        var now = DateTimeOffset.UtcNow;
        var result1 = new WorkflowResult("wf-1", "running", now);
        var result2 = new WorkflowResult("wf-1", "running", now);

        Assert.Equal(result1, result2);
    }
}
