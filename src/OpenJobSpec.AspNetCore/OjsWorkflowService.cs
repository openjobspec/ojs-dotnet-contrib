using Microsoft.Extensions.Logging;
using OpenJobSpec;
using SdkWorkflowStep = OpenJobSpec.WorkflowStep;
using SdkBatchCallbacks = OpenJobSpec.BatchCallbacks;

namespace OpenJobSpec.AspNetCore;

/// <summary>
/// Service for managing OJS workflows (chain, group, batch).
/// Registered as a singleton via DI.
/// </summary>
public sealed class OjsWorkflowService
{
    private readonly OJSClient _client;
    private readonly ILogger<OjsWorkflowService> _logger;

    public OjsWorkflowService(OJSClient client, ILogger<OjsWorkflowService> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Execute jobs sequentially as a chain workflow.
    /// Results from step N are available to step N+1.
    /// </summary>
    /// <param name="name">A human-readable name for the workflow.</param>
    /// <param name="steps">The ordered sequence of workflow steps.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result describing the created workflow.</returns>
    public async Task<WorkflowResult> ChainAsync(string name, IEnumerable<WorkflowStep> steps, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(steps);

        var sdkSteps = steps.Select(ToSdkStep).ToArray();
        var definition = Workflow.Chain(sdkSteps);

        _logger.LogInformation("Creating chain workflow '{Name}' with {StepCount} steps", name, sdkSteps.Length);

        var status = await _client.CreateWorkflowAsync(definition, ct);

        _logger.LogInformation("Chain workflow '{Name}' created with ID {WorkflowId}", name, status.Id);

        return new WorkflowResult(
            status.Id,
            status.State,
            status.CreatedAt ?? DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Execute jobs in parallel (fan-out/fan-in) as a group workflow.
    /// </summary>
    /// <param name="name">A human-readable name for the workflow.</param>
    /// <param name="steps">The set of jobs to execute concurrently.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result describing the created workflow.</returns>
    public async Task<WorkflowResult> GroupAsync(string name, IEnumerable<WorkflowStep> steps, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(steps);

        var sdkSteps = steps.Select(ToSdkStep).ToArray();
        var definition = Workflow.Group(sdkSteps);

        _logger.LogInformation("Creating group workflow '{Name}' with {JobCount} jobs", name, sdkSteps.Length);

        var status = await _client.CreateWorkflowAsync(definition, ct);

        _logger.LogInformation("Group workflow '{Name}' created with ID {WorkflowId}", name, status.Id);

        return new WorkflowResult(
            status.Id,
            status.State,
            status.CreatedAt ?? DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Enqueue jobs in parallel with completion callbacks as a batch workflow.
    /// </summary>
    /// <param name="name">A human-readable name for the workflow.</param>
    /// <param name="steps">The set of jobs to execute.</param>
    /// <param name="callbacks">Callback configuration for success/failure.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result describing the created workflow.</returns>
    public async Task<WorkflowResult> BatchAsync(string name, IEnumerable<WorkflowStep> steps, BatchCallbacks callbacks, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(callbacks);

        var sdkSteps = steps.Select(ToSdkStep).ToArray();
        var sdkCallbacks = ToSdkCallbacks(callbacks);
        var definition = Workflow.Batch(sdkCallbacks, sdkSteps);

        _logger.LogInformation("Creating batch workflow '{Name}' with {JobCount} jobs", name, sdkSteps.Length);

        var status = await _client.CreateWorkflowAsync(definition, ct);

        _logger.LogInformation("Batch workflow '{Name}' created with ID {WorkflowId}", name, status.Id);

        return new WorkflowResult(
            status.Id,
            status.State,
            status.CreatedAt ?? DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Gets the current status of a workflow.
    /// </summary>
    /// <param name="workflowId">The workflow ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The workflow status including job counts.</returns>
    public async Task<WorkflowStatusResponse> GetStatusAsync(string workflowId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);

        _logger.LogDebug("Getting status for workflow {WorkflowId}", workflowId);

        var status = await _client.GetWorkflowAsync(workflowId, ct);

        return new WorkflowStatusResponse(
            status.Id,
            status.State,
            status.JobCount,
            status.CompletedCount,
            status.FailedCount);
    }

    private static SdkWorkflowStep ToSdkStep(WorkflowStep step) => new()
    {
        Type = step.JobType,
        Args = new List<object?>(step.Args),
        Options = new EnqueueOptions
        {
            Queue = step.Queue,
            Priority = step.Priority,
        },
    };

    private static SdkBatchCallbacks ToSdkCallbacks(BatchCallbacks callbacks) => new()
    {
        OnSuccess = callbacks.OnSuccessJobType is not null
            ? new SdkWorkflowStep
            {
                Type = callbacks.OnSuccessJobType,
                Args = callbacks.OnSuccessArgs is not null ? new List<object?>(callbacks.OnSuccessArgs) : [],
            }
            : null,
        OnFailure = callbacks.OnFailureJobType is not null
            ? new SdkWorkflowStep
            {
                Type = callbacks.OnFailureJobType,
                Args = callbacks.OnFailureArgs is not null ? new List<object?>(callbacks.OnFailureArgs) : [],
            }
            : null,
    };
}

/// <summary>
/// Represents a single step in a workflow definition.
/// </summary>
/// <param name="JobType">The job type identifier (e.g., "email.send").</param>
/// <param name="Args">Arguments to pass to the job handler.</param>
/// <param name="Queue">Target queue (default: "default").</param>
/// <param name="Priority">Job priority (default: 0).</param>
public record WorkflowStep(string JobType, object[] Args, string Queue = "default", int Priority = 0);

/// <summary>
/// Result returned when a workflow is created.
/// </summary>
/// <param name="WorkflowId">The server-assigned workflow identifier.</param>
/// <param name="Status">The initial workflow status.</param>
/// <param name="CreatedAt">When the workflow was created.</param>
public record WorkflowResult(string WorkflowId, string Status, DateTimeOffset CreatedAt);

/// <summary>
/// Current status of a workflow including job completion counts.
/// </summary>
/// <param name="WorkflowId">The workflow identifier.</param>
/// <param name="Status">Current workflow state.</param>
/// <param name="TotalJobs">Total number of jobs in the workflow.</param>
/// <param name="CompletedJobs">Number of completed jobs.</param>
/// <param name="FailedJobs">Number of failed jobs.</param>
public record WorkflowStatusResponse(string WorkflowId, string Status, int TotalJobs, int CompletedJobs, int FailedJobs);

/// <summary>
/// Callback configuration for batch workflows.
/// </summary>
/// <param name="OnSuccessJobType">Job type to enqueue when all batch jobs succeed.</param>
/// <param name="OnSuccessArgs">Arguments for the success callback job.</param>
/// <param name="OnFailureJobType">Job type to enqueue when any batch job fails.</param>
/// <param name="OnFailureArgs">Arguments for the failure callback job.</param>
public record BatchCallbacks(
    string? OnSuccessJobType = null,
    object[]? OnSuccessArgs = null,
    string? OnFailureJobType = null,
    object[]? OnFailureArgs = null);
