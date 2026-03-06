using OpenJobSpec;

namespace OpenJobSpec.WorkerService;

/// <summary>
/// Interface for typed OJS job handlers that can be registered with the DI container.
/// Implement this interface and register with <c>AddOjsJobHandler&lt;T&gt;()</c>.
/// </summary>
public interface IOjsJobHandler
{
    /// <summary>
    /// Handles a job execution.
    /// </summary>
    /// <param name="context">The job context containing job data and control methods.</param>
    Task HandleAsync(JobContext context);
}
