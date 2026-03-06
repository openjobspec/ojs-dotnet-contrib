using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenJobSpec;

namespace OpenJobSpec.WorkerService;

/// <summary>
/// Extension methods for configuring OJS worker services with the .NET Generic Host.
/// </summary>
public static class OjsWorkerServiceExtensions
{
    /// <summary>
    /// Adds OJS worker services to the host, configuring client, worker, and background service.
    /// Use this for standalone worker processes that don't need ASP.NET Core.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">Action to configure worker service options.</param>
    /// <returns>The host builder for chaining.</returns>
    /// <example>
    /// <code>
    /// var builder = Host.CreateApplicationBuilder(args);
    /// builder.AddOjsWorker(opts =>
    /// {
    ///     opts.BaseUrl = "http://localhost:8080";
    ///     opts.Queues = ["emails", "notifications"];
    ///     opts.Concurrency = 5;
    /// });
    /// builder.Build().Run();
    /// </code>
    /// </example>
    public static HostApplicationBuilder AddOjsWorker(
        this HostApplicationBuilder builder,
        Action<OjsWorkerServiceOptions> configure)
    {
        var options = new OjsWorkerServiceOptions();
        configure(options);

        RegisterServices(builder.Services, options);
        return builder;
    }

    /// <summary>
    /// Adds OJS worker services from an IConfiguration section.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configuration">The configuration section containing OJS settings.</param>
    /// <returns>The host builder for chaining.</returns>
    /// <example>
    /// <code>
    /// var builder = Host.CreateApplicationBuilder(args);
    /// builder.AddOjsWorker(builder.Configuration.GetSection("Ojs"));
    /// builder.Build().Run();
    /// </code>
    /// </example>
    public static HostApplicationBuilder AddOjsWorker(
        this HostApplicationBuilder builder,
        IConfiguration configuration)
    {
        var options = new OjsWorkerServiceOptions();
        configuration.Bind(options);

        ApplyEnvironmentOverrides(options);
        RegisterServices(builder.Services, options);
        return builder;
    }

    /// <summary>
    /// Adds OJS worker services to an IHostBuilder.
    /// Use this for the older Host.CreateDefaultBuilder pattern.
    /// </summary>
    /// <param name="hostBuilder">The host builder.</param>
    /// <param name="configure">Action to configure worker service options.</param>
    /// <returns>The host builder for chaining.</returns>
    public static IHostBuilder AddOjsWorker(
        this IHostBuilder hostBuilder,
        Action<OjsWorkerServiceOptions> configure)
    {
        return hostBuilder.ConfigureServices((_, services) =>
        {
            var options = new OjsWorkerServiceOptions();
            configure(options);
            RegisterServices(services, options);
        });
    }

    /// <summary>
    /// Registers a typed job handler with the OJS worker.
    /// The handler is resolved from DI for each job execution.
    /// </summary>
    /// <typeparam name="THandler">The handler type implementing IOjsJobHandler.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="jobType">The OJS job type string (e.g., "email.send").</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOjsJobHandler<THandler>(
        this IServiceCollection services,
        string jobType)
        where THandler : class, IOjsJobHandler
    {
        services.TryAddTransient<THandler>();
        services.AddSingleton(new OjsJobHandlerRegistration(jobType, typeof(THandler)));
        return services;
    }

    private static void RegisterServices(IServiceCollection services, OjsWorkerServiceOptions options)
    {
        services.AddSingleton(options);

        services.TryAddSingleton<OJSClient>(sp =>
            new OJSClient(options.BaseUrl, new OJSClientOptions
            {
                AuthToken = options.AuthToken,
            }));

        services.TryAddSingleton<OJSWorker>(sp =>
        {
            var worker = new OJSWorker(options.BaseUrl, new OJSWorkerOptions
            {
                AuthToken = options.AuthToken,
                Queues = new List<string>(options.Queues),
                Concurrency = options.Concurrency,
                PollInterval = TimeSpan.FromSeconds(options.PollIntervalSeconds),
                HeartbeatInterval = TimeSpan.FromSeconds(options.HeartbeatIntervalSeconds),
                GracePeriod = TimeSpan.FromSeconds(options.ShutdownTimeoutSeconds),
            });

            foreach (var reg in sp.GetServices<OjsJobHandlerRegistration>())
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

        services.AddHostedService<OjsWorkerBackgroundService>();

        if (options.EnableHealthCheck)
        {
            services.AddHealthChecks()
                .Add(new HealthCheckRegistration(
                    options.HealthCheckName,
                    sp => new OjsWorkerHealthCheck(sp.GetRequiredService<OJSClient>()),
                    failureStatus: null,
                    tags: ["ojs", "worker"]));
        }
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

/// <summary>
/// Internal registration record for mapping job types to handler types.
/// </summary>
internal sealed record OjsJobHandlerRegistration(string JobType, Type HandlerType);
