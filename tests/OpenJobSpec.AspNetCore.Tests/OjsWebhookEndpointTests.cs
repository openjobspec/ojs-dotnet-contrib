using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenJobSpec.AspNetCore;

namespace OpenJobSpec.AspNetCore.Tests;

public class OjsWebhookEndpointTests
{
    [Fact]
    public void MapOjsWebhook_PreservesRouteContract()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();

        app.MapOjsWebhook("/custom/webhook");

        var endpoint = Assert.Single(((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>());

        Assert.Equal("/custom/webhook", endpoint.RoutePattern.RawText);
        Assert.Equal("OjsWebhook", endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Equal("OJS Job Webhook", endpoint.DisplayName);
        Assert.Equal(["POST"], endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods);
    }

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
        await AssertJsonBodyAsync(response, """
            {
              "status": "failed",
              "error": {
                "code": "invalid_request",
                "message": "Invalid JSON payload",
                "retryable": false
              }
            }
            """);
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
        await AssertJsonBodyAsync(response, """
            {
              "status": "failed",
              "error": {
                "code": "invalid_request",
                "message": "Missing job in request body",
                "retryable": false
              }
            }
            """);
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
        await AssertJsonBodyAsync(response, """
            {
              "status": "failed",
              "error": {
                "code": "no_handler",
                "message": "No handler registered for job type: unknown.type",
                "retryable": false
              }
            }
            """);
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
        await AssertJsonBodyAsync(response, """
            {
              "status": "completed",
              "job_id": "job-42"
            }
            """);
        Assert.True(handlerCalled);
    }

    [Fact]
    public async Task Webhook_MapsCompleteJobAndDisposesDispatchScope()
    {
        var tracker = new ScopedDispatchTracker();

        using var host = CreateTestHost(services =>
        {
            services.AddSingleton(tracker);
            services.AddScoped<ScopedWebhookDependency>();
            services.AddScoped<CapturingScopedJobHandler>();
            services.AddSingleton(new OjsHandlerRegistration("mapped.job", typeof(CapturingScopedJobHandler)));
        });
        await host.StartAsync();
        var client = host.GetTestClient();

        var content = new StringContent("""
            {
              "job": {
                "id": "job-mapped",
                "type": "mapped.job",
                "state": "not-a-state",
                "queue": "critical",
                "priority": 17,
                "attempt": 2,
                "maxAttempts": 9,
                "args": ["alpha", 42, true, null],
                "meta": {
                  "tenant": "acme",
                  "trace": 123
                }
              }
            }
            """, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/ojs/webhook", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, tracker.HandleCount);
        Assert.True(tracker.DependencyDisposed);

        var job = Assert.IsType<Job>(tracker.Context?.Job);
        Assert.Equal("job-mapped", job.Id);
        Assert.Equal("mapped.job", job.Type);
        Assert.Equal(JobState.Active, job.State);
        Assert.Equal("critical", job.Queue);
        Assert.Equal(17, job.Priority);
        Assert.Equal(2, job.Attempt);
        Assert.Equal(9, job.MaxAttempts);

        Assert.Collection(
            job.Args!,
            value => Assert.Equal("alpha", Assert.IsType<JsonElement>(value).GetString()),
            value => Assert.Equal(42, Assert.IsType<JsonElement>(value).GetInt32()),
            value => Assert.True(Assert.IsType<JsonElement>(value).GetBoolean()),
            Assert.Null);
        Assert.Equal("acme", Assert.IsType<JsonElement>(job.Meta!["tenant"]).GetString());
        Assert.Equal(123, Assert.IsType<JsonElement>(job.Meta["trace"]).GetInt32());
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
        await AssertJsonBodyAsync(response, """
            {
              "status": "failed",
              "job_id": "job-99",
              "error": {
                "code": "handler_error",
                "message": "Handler intentionally failed for testing",
                "retryable": true
              }
            }
            """);
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

    private static async Task AssertJsonBodyAsync(HttpResponseMessage response, string expected)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var actualNode = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        var expectedNode = JsonNode.Parse(expected);

        Assert.True(
            JsonNode.DeepEquals(expectedNode, actualNode),
            $"Expected: {expectedNode}{Environment.NewLine}Actual: {actualNode}");
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

internal sealed class ScopedDispatchTracker
{
    public JobContext? Context { get; set; }
    public int HandleCount { get; set; }
    public bool DependencyDisposed { get; set; }
}

internal sealed class ScopedWebhookDependency(ScopedDispatchTracker tracker) : IDisposable
{
    public void Dispose()
    {
        tracker.DependencyDisposed = true;
    }
}

internal sealed class CapturingScopedJobHandler(
    ScopedDispatchTracker tracker,
    ScopedWebhookDependency dependency) : IOjsJobHandler
{
    public Task HandleAsync(JobContext context)
    {
        Assert.False(tracker.DependencyDisposed);
        GC.KeepAlive(dependency);
        tracker.Context = context;
        tracker.HandleCount++;
        return Task.CompletedTask;
    }
}
