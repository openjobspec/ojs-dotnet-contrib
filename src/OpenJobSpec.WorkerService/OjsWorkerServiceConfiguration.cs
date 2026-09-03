using Microsoft.Extensions.Configuration;

namespace OpenJobSpec.WorkerService;

internal static class OjsWorkerServiceConfiguration
{
    internal static OjsWorkerServiceOptions Bind(IConfiguration configuration)
    {
        var options = new OjsWorkerServiceOptions();
        configuration.Bind(options);
        ApplyEnvironmentOverrides(options);
        return options;
    }

    private static void ApplyEnvironmentOverrides(OjsWorkerServiceOptions options)
    {
        var envUrl = Environment.GetEnvironmentVariable("OJS_URL");
        if (!string.IsNullOrEmpty(envUrl))
            options.BaseUrl = envUrl;

        var envToken = Environment.GetEnvironmentVariable("OJS_AUTH_TOKEN");
        if (!string.IsNullOrEmpty(envToken))
            options.AuthToken = envToken;

        var envQueues = Environment.GetEnvironmentVariable("OJS_QUEUES");
        if (!string.IsNullOrEmpty(envQueues))
            options.Queues = envQueues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var envConcurrency = Environment.GetEnvironmentVariable("OJS_CONCURRENCY");
        if (int.TryParse(envConcurrency, out var concurrency))
            options.Concurrency = concurrency;
    }
}
