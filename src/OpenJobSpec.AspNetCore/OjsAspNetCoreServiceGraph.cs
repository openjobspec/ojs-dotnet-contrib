using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenJobSpec;

namespace OpenJobSpec.AspNetCore;

internal static class OjsAspNetCoreServiceGraph
{
    internal static IServiceCollection Register(IServiceCollection services, OjsOptions options)
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

        services.AddHostedService<OjsWorkerHostedService>();

        return services;
    }
}
