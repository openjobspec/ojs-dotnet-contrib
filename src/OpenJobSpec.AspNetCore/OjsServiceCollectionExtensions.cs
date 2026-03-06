using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenJobSpec;

namespace OpenJobSpec.AspNetCore;

/// <summary>
/// Extension methods for registering OJS services with the ASP.NET Core DI container.
/// </summary>
public static class OjsServiceCollectionExtensions
{
    /// <summary>
    /// Adds OJS client and worker services to the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure OJS options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOjs(this IServiceCollection services, Action<OjsOptions> configure)
    {
        var options = new OjsOptions();
        configure(options);

        return services.AddOjsCore(options);
    }

    /// <summary>
    /// Adds OJS client and worker services from an IConfiguration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section containing OJS settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOjs(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new OjsOptions();
        configuration.Bind(options);

        // Support environment variable overrides
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

        return services.AddOjsCore(options);
    }

    /// <summary>
    /// Registers a typed job handler with the OJS worker.
    /// The handler is resolved from DI for each job execution.
    /// </summary>
    /// <typeparam name="THandler">The handler type implementing IOjsJobHandler.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="jobType">The OJS job type string (e.g., "email.send").</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOjsHandler<THandler>(this IServiceCollection services, string jobType)
        where THandler : class, IOjsJobHandler
    {
        services.TryAddTransient<THandler>();
        services.AddSingleton(new OjsHandlerRegistration(jobType, typeof(THandler)));
        return services;
    }

    private static IServiceCollection AddOjsCore(this IServiceCollection services, OjsOptions options)
    {
        services.AddSingleton(options);

        services.TryAddSingleton<OJSClient>(sp =>
        {
            return new OJSClient(options.BaseUrl, new OJSClientOptions
            {
                AuthToken = options.AuthToken,
            });
        });

        services.TryAddSingleton<OJSWorker>(sp =>
        {
            var worker = new OJSWorker(options.BaseUrl, new OJSWorkerOptions
            {
                AuthToken = options.AuthToken,
                Queues = new List<string>(options.Worker.Queues),
                Concurrency = options.Worker.Concurrency,
            });

            // Auto-register all handler registrations
            foreach (var reg in sp.GetServices<OjsHandlerRegistration>())
            {
                worker.Register(reg.JobType, async ctx =>
                {
                    using var scope = sp.CreateScope();
                    var handler = (IOjsJobHandler)scope.ServiceProvider.GetRequiredService(reg.HandlerType);
                    await handler.HandleAsync(ctx);
                });
            }

            return worker;
        });

        // Register OJSWorker as a hosted service so it starts/stops with the app
        services.AddHostedService<OjsWorkerHostedService>();

        return services;
    }
}

/// <summary>
/// Extension methods for adding OJS health checks.
/// </summary>
public static class OjsHealthCheckExtensions
{
    /// <summary>
    /// Adds an OJS backend health check.
    /// </summary>
    public static IHealthChecksBuilder AddOjs(this IHealthChecksBuilder builder, string name = "ojs", HealthStatus? failureStatus = null)
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new OjsHealthCheck(sp.GetRequiredService<OJSClient>()),
            failureStatus,
            tags: ["ojs", "backend"]
        ));
    }
}

/// <summary>
/// Internal registration record for mapping job types to handler types.
/// </summary>
internal sealed record OjsHandlerRegistration(string JobType, Type HandlerType);
