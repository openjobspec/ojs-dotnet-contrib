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

        return OjsAspNetCoreServiceGraph.Register(services, options);
    }

    /// <summary>
    /// Adds OJS client and worker services from an IConfiguration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section containing OJS settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOjs(this IServiceCollection services, IConfiguration configuration)
    {
        var options = OjsAspNetCoreConfiguration.Bind(configuration);
        return OjsAspNetCoreServiceGraph.Register(services, options);
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

    /// <summary>
    /// Adds OJS workflow service to the DI container.
    /// Requires <see cref="AddOjs(IServiceCollection, Action{OjsOptions})"/> to be called first.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOjsWorkflows(this IServiceCollection services)
    {
        services.TryAddSingleton<OjsWorkflowService>();
        return services;
    }

    /// <summary>
    /// Adds OJS event subscription service to the DI container.
    /// Requires <see cref="AddOjs(IServiceCollection, Action{OjsOptions})"/> to be called first.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOjsEventSubscription(this IServiceCollection services)
    {
        services.TryAddSingleton<OjsEventSubscriptionService>();
        return services;
    }

    /// <summary>
    /// Registers a typed event handler with the OJS event subscription service.
    /// The handler is resolved from DI when events arrive.
    /// </summary>
    /// <typeparam name="THandler">The handler type implementing <see cref="IOjsEventHandler"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOjsEventHandler<THandler>(this IServiceCollection services)
        where THandler : class, IOjsEventHandler
    {
        services.TryAddTransient<THandler>();
        services.AddSingleton(new OjsEventHandlerRegistration(typeof(THandler)));
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

/// <summary>
/// Internal registration record for mapping event handler types.
/// </summary>
internal sealed record OjsEventHandlerRegistration(Type HandlerType);
