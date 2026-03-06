using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenJobSpec;

namespace OpenJobSpec.AspNetCore;

/// <summary>
/// Middleware that makes the OJS client available via HttpContext.Items
/// for convenient access in controllers and minimal API handlers.
/// </summary>
internal sealed class OjsMiddleware
{
    private readonly RequestDelegate _next;

    public OjsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var client = context.RequestServices.GetService<OJSClient>();
        if (client != null)
        {
            context.Items["ojs:client"] = client;
        }

        await _next(context);
    }
}

/// <summary>
/// Extension methods for adding OJS middleware to the ASP.NET Core pipeline.
/// </summary>
public static class OjsApplicationBuilderExtensions
{
    /// <summary>
    /// Adds OJS middleware to the pipeline, making the OJS client accessible via HttpContext extensions.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <example>
    /// <code>
    /// app.UseOjs();
    ///
    /// app.MapPost("/api/send-email", async (HttpContext ctx) =>
    /// {
    ///     var client = ctx.GetOjsClient();
    ///     await client.EnqueueAsync("email.send", new[] { "user@example.com" });
    ///     return Results.Ok();
    /// });
    /// </code>
    /// </example>
    public static IApplicationBuilder UseOjs(this IApplicationBuilder app)
    {
        return app.UseMiddleware<OjsMiddleware>();
    }
}

/// <summary>
/// Extension methods on HttpContext for convenient OJS access.
/// </summary>
public static class OjsHttpContextExtensions
{
    /// <summary>
    /// Gets the OJS client from HttpContext.
    /// Requires <c>app.UseOjs()</c> or OJS services to be registered.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The OJS client instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when OJS client is not available.</exception>
    public static OJSClient GetOjsClient(this HttpContext context)
    {
        if (context.Items.TryGetValue("ojs:client", out var client) && client is OJSClient ojsClient)
        {
            return ojsClient;
        }

        // Fallback to DI
        var diClient = context.RequestServices.GetService<OJSClient>();
        if (diClient != null) return diClient;

        throw new InvalidOperationException(
            "OJS client not found. Ensure services.AddOjs() is called in ConfigureServices " +
            "and app.UseOjs() is called in Configure.");
    }
}
