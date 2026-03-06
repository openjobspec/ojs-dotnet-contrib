using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenJobSpec;

namespace OpenJobSpec.WorkerService;

/// <summary>
/// Health check that validates connectivity to the OJS backend.
/// </summary>
internal sealed class OjsWorkerHealthCheck : IHealthCheck
{
    private readonly OJSClient _client;

    public OjsWorkerHealthCheck(OJSClient client)
    {
        _client = client;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var health = await _client.HealthAsync(cancellationToken);
            if (health.Status == "ok" || health.Status == "healthy")
            {
                return HealthCheckResult.Healthy($"OJS backend is healthy (version: {health.Version})");
            }

            return HealthCheckResult.Degraded($"OJS backend reports status: {health.Status}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("OJS backend is unreachable", ex);
        }
    }
}
