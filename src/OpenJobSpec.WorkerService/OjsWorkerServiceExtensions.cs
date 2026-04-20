using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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

        OjsWorkerServiceGraph.Register(builder.Services, options);
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
        var options = OjsWorkerServiceConfiguration.Bind(configuration);
        OjsWorkerServiceGraph.Register(builder.Services, options);
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
            OjsWorkerServiceGraph.Register(services, options);
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

    /// <summary>
    /// Registers a typed event listener for OJS events.
    /// The listener is resolved from DI when events are dispatched.
    /// </summary>
    /// <typeparam name="TListener">The listener type implementing IOjsEventListener.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOjsEventListener<TListener>(
        this IServiceCollection services)
        where TListener : class, IOjsEventListener
    {
        services.AddTransient<IOjsEventListener, TListener>();
        return services;
    }

    /// <summary>
    /// Registers a cron schedule that enqueues jobs on the specified schedule.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">Unique name for this cron schedule.</param>
    /// <param name="cronExpression">Standard 5-field cron expression (minute hour day month weekday).</param>
    /// <param name="jobType">The OJS job type to enqueue when the schedule triggers.</param>
    /// <param name="args">Optional job arguments.</param>
    /// <param name="queue">Target queue (default: "default").</param>
    /// <param name="timezone">Optional IANA timezone for schedule evaluation.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOjsCronSchedule(
        this IServiceCollection services,
        string name,
        string cronExpression,
        string jobType,
        object[]? args = null,
        string queue = "default",
        string? timezone = null)
    {
        services.AddSingleton(new OjsCronRegistration(name, cronExpression, jobType, args, queue, timezone));
        return services;
    }

    /// <summary>
    /// Adds OJS encryption support for job argument encryption/decryption.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure encryption options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOjsEncryption(
        this IServiceCollection services,
        Action<OjsEncryptionServiceOptions> configure)
    {
        var options = new OjsEncryptionServiceOptions();
        configure(options);
        services.AddSingleton(options);
        services.AddSingleton<OjsEncryptionService>();
        return services;
    }

}

/// <summary>
/// Internal registration record for mapping job types to handler types.
/// </summary>
internal sealed record OjsJobHandlerRegistration(string JobType, Type HandlerType);
