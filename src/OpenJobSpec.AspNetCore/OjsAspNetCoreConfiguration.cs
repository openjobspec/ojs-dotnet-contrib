using Microsoft.Extensions.Configuration;

namespace OpenJobSpec.AspNetCore;

internal static class OjsAspNetCoreConfiguration
{
    internal static OjsOptions Bind(IConfiguration configuration)
    {
        var options = new OjsOptions();
        configuration.Bind(options);
        ApplyEnvironmentOverrides(options);
        return options;
    }

    private static void ApplyEnvironmentOverrides(OjsOptions options)
    {
        var envUrl = Environment.GetEnvironmentVariable("OJS_URL");
        if (!string.IsNullOrEmpty(envUrl))
            options.BaseUrl = envUrl;

        var envToken = Environment.GetEnvironmentVariable("OJS_AUTH_TOKEN");
        if (!string.IsNullOrEmpty(envToken))
            options.AuthToken = envToken;

        var envQueues = Environment.GetEnvironmentVariable("OJS_QUEUES");
        if (!string.IsNullOrEmpty(envQueues))
            options.Worker.Queues = envQueues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var envConcurrency = Environment.GetEnvironmentVariable("OJS_CONCURRENCY");
        if (int.TryParse(envConcurrency, out var concurrency))
            options.Worker.Concurrency = concurrency;
    }
}
