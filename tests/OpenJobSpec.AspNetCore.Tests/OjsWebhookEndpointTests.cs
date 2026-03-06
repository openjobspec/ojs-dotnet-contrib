using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenJobSpec.AspNetCore;

namespace OpenJobSpec.AspNetCore.Tests;

public class OjsWebhookEndpointTests
{
    private static IHost CreateTestHost(Action<IServiceCollection>? configureServices = null)
    {
        return new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
                    configureServices?.Invoke(services);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapOjsWebhook("/ojs/webhook");
                    });
                });
            })
            .Build();
    }

    [Fact]
    public async Task Webhook_InvalidJson_Returns400()
    {
        using var host = CreateTestHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        var content = new StringContent("not valid json", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/ojs/webhook", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid_request", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Webhook_MissingJob_Returns400()
    {
        using var host = CreateTestHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        var payload = JsonSerializer.Serialize(new { deliveryId = "test" });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/ojs/webhook", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Missing job", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Webhook_UnregisteredJobType_Returns422()
    {
        using var host = CreateTestHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        var payload = JsonSerializer.Serialize(new
        {
            job = new { id = "job-1", type = "unknown.type", state = "active", queue = "default" }
        });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/ojs/webhook", content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("no_handler", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Webhook_ValidJob_DispatchesToHandler()
    {
        var handlerCalled = false;

        using var host = CreateTestHost(services =>
        {
            services.AddSingleton(new OjsHandlerRegistration("email.send", typeof(TrackingJobHandler)));
            services.AddTransient<TrackingJobHandler>();
            // Set shared state via a callback
            TrackingJobHandler.OnHandleCallback = _ => handlerCalled = true;
        });
        await host.StartAsync();
        var client = host.GetTestClient();

        var payload = JsonSerializer.Serialize(new
        {
            job = new { id = "job-42", type = "email.send", state = "active", queue = "default", args = new[] { "test@example.com" } }
        });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/ojs/webhook", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("completed", body, StringComparison.OrdinalIgnoreCase);
        Assert.True(handlerCalled);
    }

    [Fact]
    public async Task Webhook_HandlerThrows_Returns500()
    {
        using var host = CreateTestHost(services =>
        {
            services.AddSingleton(new OjsHandlerRegistration("fail.job", typeof(FailingJobHandler)));
            services.AddTransient<FailingJobHandler>();
        });
        await host.StartAsync();
        var client = host.GetTestClient();

        var payload = JsonSerializer.Serialize(new
        {
            job = new { id = "job-99", type = "fail.job", state = "active", queue = "default" }
        });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/ojs/webhook", content);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("handler_error", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("retryable", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Webhook_EmptyBody_Returns400()
    {
        using var host = CreateTestHost();
        await host.StartAsync();
        var client = host.GetTestClient();

        var content = new StringContent("", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/ojs/webhook", content);

        // Empty body deserializes to null Job, should return 400
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

internal class TrackingJobHandler : IOjsJobHandler
{
    public static Action<JobContext>? OnHandleCallback { get; set; }

    public Task HandleAsync(JobContext context)
    {
        OnHandleCallback?.Invoke(context);
        return Task.CompletedTask;
    }
}

internal class FailingJobHandler : IOjsJobHandler
{
    public Task HandleAsync(JobContext context)
    {
        throw new InvalidOperationException("Handler intentionally failed for testing");
    }
}
