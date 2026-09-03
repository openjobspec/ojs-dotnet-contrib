using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenJobSpec.AspNetCore;

namespace OpenJobSpec.AspNetCore.Tests;

public class ServiceRegistrationTests
{
    [Fact]
    public void AddOjs_PreservesDescriptorOrderLifetimesFactoriesAndReturnValue()
    {
        var services = new ServiceCollection();

        var returned = services.AddOjs(opts => opts.BaseUrl = "http://test:8080");

        Assert.Same(services, returned);
        Assert.Collection(
            services,
            descriptor => AssertInstanceDescriptor<OjsOptions>(descriptor),
            descriptor => AssertFactoryDescriptor<OJSClient>(descriptor),
            descriptor => AssertFactoryDescriptor<OJSWorker>(descriptor),
            descriptor => AssertTypeDescriptor<IHostedService, OjsWorkerHostedService>(descriptor));
    }

    [Fact]
    public void AddOjs_RegistersOjsOptions()
    {
        var services = new ServiceCollection();
        services.AddOjs(opts =>
        {
            opts.BaseUrl = "http://test:8080";
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetService<OjsOptions>();

        Assert.NotNull(options);
        Assert.Equal("http://test:8080", options.BaseUrl);
    }

    [Fact]
    public void AddOjs_WithAuthToken_SetsToken()
    {
        var services = new ServiceCollection();
        services.AddOjs(opts =>
        {
            opts.BaseUrl = "http://test:8080";
            opts.AuthToken = "my-token";
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<OjsOptions>();

        Assert.Equal("my-token", options.AuthToken);
    }

    [Fact]
    public void AddOjs_WithWorkerConfig_SetsWorkerOptions()
    {
        var services = new ServiceCollection();
        services.AddOjs(opts =>
        {
            opts.BaseUrl = "http://test:8080";
            opts.Worker.Queues = ["high", "low"];
            opts.Worker.Concurrency = 5;
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<OjsOptions>();

        Assert.Equal(new[] { "high", "low" }, options.Worker.Queues);
        Assert.Equal(5, options.Worker.Concurrency);
    }

    [Fact]
    public void AddOjsHandler_RegistersHandlerRegistration()
    {
        var services = new ServiceCollection();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsHandler<TestJobHandler>("test.job");

        var provider = services.BuildServiceProvider();
        var registrations = provider.GetServices<OjsHandlerRegistration>();

        Assert.Contains(registrations, r => r.JobType == "test.job" && r.HandlerType == typeof(TestJobHandler));
    }

    [Fact]
    public void AddOjsHandler_RegistersHandlerAsTransient()
    {
        var services = new ServiceCollection();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsHandler<TestJobHandler>("test.job");

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TestJobHandler));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
    }

    [Fact]
    public void AddOjsHandler_MultipleHandlers_AllRegistered()
    {
        var services = new ServiceCollection();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        services.AddOjsHandler<TestJobHandler>("test.job.one");
        services.AddOjsHandler<AnotherJobHandler>("test.job.two");

        var provider = services.BuildServiceProvider();
        var registrations = provider.GetServices<OjsHandlerRegistration>().ToList();

        Assert.Equal(2, registrations.Count);
    }

    private static void AssertInstanceDescriptor<TService>(ServiceDescriptor descriptor)
    {
        Assert.Equal(typeof(TService), descriptor.ServiceType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.IsType<TService>(descriptor.ImplementationInstance);
        Assert.Null(descriptor.ImplementationFactory);
        Assert.Null(descriptor.ImplementationType);
    }

    private static void AssertFactoryDescriptor<TService>(ServiceDescriptor descriptor)
    {
        Assert.Equal(typeof(TService), descriptor.ServiceType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Null(descriptor.ImplementationInstance);
        Assert.Null(descriptor.ImplementationType);
    }

    private static void AssertTypeDescriptor<TService, TImplementation>(ServiceDescriptor descriptor)
    {
        Assert.Equal(typeof(TService), descriptor.ServiceType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
        Assert.Null(descriptor.ImplementationFactory);
        Assert.Null(descriptor.ImplementationInstance);
    }
}

// Test handler implementations
internal class TestJobHandler : IOjsJobHandler
{
    public Task HandleAsync(JobContext context) => Task.CompletedTask;
}

internal class AnotherJobHandler : IOjsJobHandler
{
    public Task HandleAsync(JobContext context) => Task.CompletedTask;
}
