using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenJobSpec.AspNetCore;

namespace OpenJobSpec.AspNetCore.Tests;

public class OjsMiddlewareTests
{
    [Fact]
    public async Task Middleware_SetsClientInHttpContextItems()
    {
        var services = new ServiceCollection();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider };
        var middlewareCalled = false;

        var middleware = new OjsMiddleware(ctx =>
        {
            middlewareCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(middlewareCalled);
        Assert.True(context.Items.ContainsKey("ojs:client"));
        Assert.IsType<OJSClient>(context.Items["ojs:client"]);
    }

    [Fact]
    public async Task Middleware_CallsNextDelegate()
    {
        var services = new ServiceCollection();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider };
        var nextCalled = false;

        var middleware = new OjsMiddleware(ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Middleware_WithNoClient_DoesNotSetItem()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider };

        var middleware = new OjsMiddleware(ctx => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.False(context.Items.ContainsKey("ojs:client"));
    }

    [Fact]
    public void GetOjsClient_ReturnsClientFromItems()
    {
        var client = new OJSClient("http://test:8080");
        var context = new DefaultHttpContext();
        context.Items["ojs:client"] = client;

        var result = context.GetOjsClient();

        Assert.Same(client, result);
    }

    [Fact]
    public void GetOjsClient_FallsBackToDI()
    {
        var services = new ServiceCollection();
        services.AddOjs(opts => opts.BaseUrl = "http://test:8080");
        var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider };
        // Items does NOT contain the client — should fall back to DI

        var result = context.GetOjsClient();

        Assert.NotNull(result);
        Assert.IsType<OJSClient>(result);
    }

    [Fact]
    public void GetOjsClient_ThrowsWhenNothingRegistered()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider };

        var ex = Assert.Throws<InvalidOperationException>(() => context.GetOjsClient());
        Assert.Contains("OJS client not found", ex.Message);
    }
}
