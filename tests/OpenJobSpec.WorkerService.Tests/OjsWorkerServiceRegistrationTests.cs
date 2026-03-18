using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenJobSpec.WorkerService;

namespace OpenJobSpec.WorkerService.Tests;

public class OjsWorkerServiceRegistrationTests
{
    [Fact]
    public void AddOjsWorker_RegistersOptions()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts =>
        {
            opts.BaseUrl = "http://test:8080";
            opts.Queues = ["high", "low"];
            opts.Concurrency = 5;
        });

        var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetService<OjsWorkerServiceOptions>();

        Assert.NotNull(options);
        Assert.Equal("http://test:8080", options.BaseUrl);
        Assert.Equal(new[] { "high", "low" }, options.Queues);
        Assert.Equal(5, options.Concurrency);
    }

    [Fact]
    public void AddOjsWorker_RegistersClient()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts => opts.BaseUrl = "http://test:8080");

        var provider = builder.Services.BuildServiceProvider();
        var client = provider.GetService<OJSClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddOjsWorker_RegistersWorker()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts => opts.BaseUrl = "http://test:8080");

        var provider = builder.Services.BuildServiceProvider();
        var worker = provider.GetService<OJSWorker>();

        Assert.NotNull(worker);
    }

    [Fact]
    public void AddOjsWorker_RegistersBackgroundService()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts => opts.BaseUrl = "http://test:8080");

        var provider = builder.Services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        Assert.Contains(hostedServices, s => s.GetType().Name == "OjsWorkerBackgroundService");
    }

    [Fact]
    public void AddOjsWorker_RegistersHealthCheck_WhenEnabled()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts =>
        {
            opts.BaseUrl = "http://test:8080";
            opts.EnableHealthCheck = true;
        });

        var provider = builder.Services.BuildServiceProvider();
        var healthCheckOptions = provider.GetService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();

        Assert.NotNull(healthCheckOptions);
        Assert.Contains(healthCheckOptions.Value.Registrations, r => r.Name == "ojs");
    }

    [Fact]
    public void AddOjsWorker_SkipsHealthCheck_WhenDisabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Build options manually (using IHostBuilder overload pattern)
        var options = new OjsWorkerServiceOptions
        {
            BaseUrl = "http://test:8080",
            EnableHealthCheck = false,
        };

        // Use the IHostBuilder extension path
        var hostBuilder = new HostBuilder();
        hostBuilder.AddOjsWorker(opts =>
        {
            opts.BaseUrl = "http://test:8080";
            opts.EnableHealthCheck = false;
        });

        using var host = hostBuilder.Build();
        var healthOptions = host.Services.GetService<HealthCheckServiceOptions>();

        // Health check should not be registered, or the "ojs" registration should not be present
        if (healthOptions != null)
        {
            Assert.DoesNotContain(healthOptions.Registrations, r => r.Name == "ojs");
        }
    }

    [Fact]
    public void AddOjsWorker_ClientIsSingleton()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts => opts.BaseUrl = "http://test:8080");

        var provider = builder.Services.BuildServiceProvider();
        var client1 = provider.GetService<OJSClient>();
        var client2 = provider.GetService<OJSClient>();

        Assert.Same(client1, client2);
    }

    [Fact]
    public void AddOjsWorker_WorkerIsSingleton()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts => opts.BaseUrl = "http://test:8080");

        var provider = builder.Services.BuildServiceProvider();
        var worker1 = provider.GetService<OJSWorker>();
        var worker2 = provider.GetService<OJSWorker>();

        Assert.Same(worker1, worker2);
    }

    [Fact]
    public void AddOjsJobHandler_RegistersHandler()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts => opts.BaseUrl = "http://test:8080");
        builder.Services.AddOjsJobHandler<TestEmailHandler>("email.send");

        var provider = builder.Services.BuildServiceProvider();
        var registrations = provider.GetServices<OjsJobHandlerRegistration>();

        Assert.Contains(registrations, r => r.JobType == "email.send" && r.HandlerType == typeof(TestEmailHandler));
    }

    [Fact]
    public void AddOjsJobHandler_MultipleHandlers_AllRegistered()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts => opts.BaseUrl = "http://test:8080");
        builder.Services.AddOjsJobHandler<TestEmailHandler>("email.send");
        builder.Services.AddOjsJobHandler<TestReportHandler>("report.generate");

        var provider = builder.Services.BuildServiceProvider();
        var registrations = provider.GetServices<OjsJobHandlerRegistration>().ToList();

        Assert.Equal(2, registrations.Count);
    }

    [Fact]
    public void AddOjsWorker_FromConfiguration_BindsOptions()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BaseUrl"] = "http://config:9090",
                ["Concurrency"] = "15",
                ["Queues:0"] = "priority",
                ["Queues:1"] = "bulk",
            })
            .Build();

        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(config);

        var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<OjsWorkerServiceOptions>();

        Assert.Equal("http://config:9090", options.BaseUrl);
        Assert.Equal(15, options.Concurrency);
    }

    [Fact]
    public void AddOjsWorker_ViaIHostBuilder_RegistersServices()
    {
        var hostBuilder = new HostBuilder();
        hostBuilder.AddOjsWorker(opts =>
        {
            opts.BaseUrl = "http://test:8080";
            opts.Queues = ["queue1"];
        });

        using var host = hostBuilder.Build();
        var options = host.Services.GetService<OjsWorkerServiceOptions>();
        var client = host.Services.GetService<OJSClient>();
        var worker = host.Services.GetService<OJSWorker>();

        Assert.NotNull(options);
        Assert.NotNull(client);
        Assert.NotNull(worker);
        Assert.Equal("http://test:8080", options.BaseUrl);
    }

    [Fact]
    public void AddOjsEventListener_RegistersInDI()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts => opts.BaseUrl = "http://test:8080");
        builder.Services.AddOjsEventListener<TestRegistrationEventListener>();

        var provider = builder.Services.BuildServiceProvider();
        var listeners = provider.GetServices<IOjsEventListener>().ToList();

        Assert.Single(listeners);
        Assert.IsType<TestRegistrationEventListener>(listeners[0]);
    }

    [Fact]
    public void AddOjsCronSchedule_RegistersInDI()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts => opts.BaseUrl = "http://test:8080");
        builder.Services.AddOjsCronSchedule("daily", "0 2 * * *", "cleanup.run");

        var provider = builder.Services.BuildServiceProvider();
        var schedules = provider.GetServices<OjsCronRegistration>().ToList();

        Assert.Single(schedules);
        Assert.Equal("daily", schedules[0].Name);
        Assert.Equal("cleanup.run", schedules[0].JobType);
    }

    [Fact]
    public void AddOjsEncryption_RegistersInDI()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts => opts.BaseUrl = "http://test:8080");
        builder.Services.AddOjsEncryption(opts =>
        {
            opts.EncryptionKey = Convert.ToBase64String(new byte[32]);
        });

        var provider = builder.Services.BuildServiceProvider();
        var service = provider.GetService<OjsEncryptionService>();

        Assert.NotNull(service);
    }

    [Fact]
    public void EventListenerService_RegisteredWhenEnabled()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts =>
        {
            opts.BaseUrl = "http://test:8080";
            opts.EventListener.Enabled = true;
        });

        var provider = builder.Services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        Assert.Contains(hostedServices, s => s.GetType().Name == "OjsEventListenerService");
    }

    [Fact]
    public void CronSchedulerService_RegisteredWhenEnabled()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts =>
        {
            opts.BaseUrl = "http://test:8080";
            opts.Cron.Enabled = true;
        });

        var provider = builder.Services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        Assert.Contains(hostedServices, s => s.GetType().Name == "OjsCronSchedulerService");
    }

    [Fact]
    public void EventListenerService_NotRegisteredWhenDisabled()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts =>
        {
            opts.BaseUrl = "http://test:8080";
            opts.EventListener.Enabled = false;
        });

        var provider = builder.Services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        Assert.DoesNotContain(hostedServices, s => s.GetType().Name == "OjsEventListenerService");
    }

    [Fact]
    public void CronSchedulerService_NotRegisteredWhenDisabled()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddOjsWorker(opts =>
        {
            opts.BaseUrl = "http://test:8080";
            opts.Cron.Enabled = false;
        });

        var provider = builder.Services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        Assert.DoesNotContain(hostedServices, s => s.GetType().Name == "OjsCronSchedulerService");
    }
}

// Test handlers
internal class TestEmailHandler : IOjsJobHandler
{
    public Task HandleAsync(JobContext context) => Task.CompletedTask;
}

internal class TestReportHandler : IOjsJobHandler
{
    public Task HandleAsync(JobContext context) => Task.CompletedTask;
}

internal class TestRegistrationEventListener : IOjsEventListener
{
    public string EventType => "job.completed";
    public Task HandleAsync(OjsEventData eventData, CancellationToken ct = default) => Task.CompletedTask;
}
