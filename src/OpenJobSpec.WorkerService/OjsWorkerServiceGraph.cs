using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenJobSpec;

namespace OpenJobSpec.WorkerService;

internal static class OjsWorkerServiceGraph
{
    internal static void Register(IServiceCollection services, OjsWorkerServiceOptions options)
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

        if (options.EventListener.Enabled)
        {
            services.AddSingleton(options.EventListener);
            services.AddHostedService<OjsEventListenerService>();
        }

        if (options.Cron.Enabled)
        {
            services.AddSingleton(options.Cron);
            services.AddHostedService<OjsCronSchedulerService>();
        }

        if (!string.IsNullOrEmpty(options.Encryption.EncryptionKey) ||
            !string.IsNullOrEmpty(options.Encryption.CodecServerUrl))
        {
            services.TryAddSingleton(options.Encryption);
            services.TryAddSingleton<OjsEncryptionService>();
        }
    }
}
