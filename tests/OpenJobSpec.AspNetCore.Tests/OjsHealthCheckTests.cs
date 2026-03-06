using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenJobSpec.AspNetCore;

namespace OpenJobSpec.AspNetCore.Tests;

public class OjsHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenBackendHealthy_ReturnsHealthy()
    {
        var handler = new FakeHttpMessageHandler(
            """{"status":"ok","version":"0.2.0"}""",
            System.Net.HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var client = new OJSClient("http://test:8080", new OJSClientOptions { HttpClient = httpClient });
        var healthCheck = new OjsHealthCheck(client);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("healthy", result.Description!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenStatusHealthy_ReturnsHealthy()
    {
        var handler = new FakeHttpMessageHandler(
            """{"status":"healthy","version":"0.2.0"}""",
            System.Net.HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var client = new OJSClient("http://test:8080", new OJSClientOptions { HttpClient = httpClient });
        var healthCheck = new OjsHealthCheck(client);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenDegraded_ReturnsDegraded()
    {
        var handler = new FakeHttpMessageHandler(
            """{"status":"degraded","version":"0.2.0"}""",
            System.Net.HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var client = new OJSClient("http://test:8080", new OJSClientOptions { HttpClient = httpClient });
        var healthCheck = new OjsHealthCheck(client);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("degraded", result.Description!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenUnreachable_ReturnsUnhealthy()
    {
        var handler = new FakeHttpMessageHandler(throwException: new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler);
        var client = new OJSClient("http://test:8080", new OJSClientOptions { HttpClient = httpClient });
        var healthCheck = new OjsHealthCheck(client);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("unreachable", result.Description!, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Exception);
    }
}

/// <summary>
/// Fake HTTP handler for testing without real HTTP calls.
/// </summary>
internal class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly string? _responseContent;
    private readonly System.Net.HttpStatusCode _statusCode;
    private readonly Exception? _exception;

    public FakeHttpMessageHandler(
        string? responseContent = null,
        System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK,
        Exception? throwException = null)
    {
        _responseContent = responseContent;
        _statusCode = statusCode;
        _exception = throwException;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_exception is not null)
            throw _exception;

        var response = new HttpResponseMessage(_statusCode);
        if (_responseContent is not null)
            response.Content = new StringContent(_responseContent, System.Text.Encoding.UTF8, "application/json");

        return Task.FromResult(response);
    }
}
