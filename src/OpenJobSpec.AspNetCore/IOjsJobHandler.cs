namespace OpenJobSpec.AspNetCore;

/// <summary>
/// Interface for typed OJS job handlers that can be registered with the DI container.
/// </summary>
public interface IOjsJobHandler
{
    /// <summary>
    /// Handles a job execution.
    /// </summary>
    /// <param name="context">The job context containing job data and control methods.</param>
    Task HandleAsync(JobContext context);
}
