using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenJobSpec.AspNetCore;

namespace OpenJobSpec.AspNetCore.Tests;

public class OjsWorkerHostedServiceTests
{
    [Fact]
    public void HostedService_IsRegisteredByAddOjs()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");

        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        Assert.Contains(hostedServices, s => s.GetType().Name == "OjsWorkerHostedService");
    }

    [Fact]
    public async Task StartAsync_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");

        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();
        var workerService = hostedServices.First(s => s.GetType().Name == "OjsWorkerHostedService");

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately to prevent actual polling
        // StartAsync fires a background task, so even a cancelled token shouldn't throw
        await workerService.StartAsync(cts.Token);
    }

    [Fact]
    public async Task StopAsync_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");

        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();
        var workerService = hostedServices.First(s => s.GetType().Name == "OjsWorkerHostedService");

        await workerService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void HostedService_UsesConfiguredWorkerOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts =>
        {
            opts.BaseUrl = "http://test:8080";
            opts.Worker.Queues = ["emails", "notifications"];
            opts.Worker.Concurrency = 5;
        });

        var provider = services.BuildServiceProvider();

        // Verify the OjsOptions are available (used by the hosted service for logging)
        var options = provider.GetService<OjsOptions>();
        Assert.NotNull(options);
        Assert.Equal(new[] { "emails", "notifications" }, options.Worker.Queues);
        Assert.Equal(5, options.Worker.Concurrency);
    }

    [Fact]
    public void Worker_IsRegisteredAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");

        var provider = services.BuildServiceProvider();
        var worker1 = provider.GetService<OJSWorker>();
        var worker2 = provider.GetService<OJSWorker>();

        Assert.NotNull(worker1);
        Assert.Same(worker1, worker2);
    }
}
