# OJS .NET Contrib

[![NuGet](https://img.shields.io/nuget/v/OpenJobSpec.AspNetCore)](https://www.nuget.org/packages/OpenJobSpec.AspNetCore)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)

.NET framework integrations for the [Open Job Spec](https://openjobspec.org) .NET SDK.

## Packages

| Package | Description | Use Case |
|---------|-------------|----------|
| **OpenJobSpec.AspNetCore** | ASP.NET Core integration | Web apps that also process background jobs |
| **OpenJobSpec.WorkerService** | .NET Worker Service integration | Standalone background job processors |

---

## OpenJobSpec.AspNetCore

ASP.NET Core integration providing dependency injection, health checks, middleware, and hosted service integration for seamless background job processing in web applications.

### Installation

```bash
dotnet add package OpenJobSpec.AspNetCore
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register OJS client and worker with DI
builder.Services.AddOjs(options =>
{
    options.BaseUrl = "http://localhost:8080";
    options.AuthToken = builder.Configuration["OJS_AUTH_TOKEN"];
});

// Add health check for OJS backend connectivity
builder.Services.AddHealthChecks()
    .AddOjs();

// Register job handlers
builder.Services.AddOjsHandler<EmailSendHandler>("email.send");

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();
```

## Features

- **`AddOjs()`** — Registers `OJSClient` and `OJSWorker` as singleton services
- **`AddOjsHandler<T>()`** — Registers typed job handlers with the worker
- **`AddOjs()` health check** — Monitors OJS backend connectivity
- **Hosted service** — Worker runs as a background hosted service, integrates with ASP.NET Core lifecycle
- **Configuration binding** — Bind from `appsettings.json` or environment variables
- **`UseOjs()` middleware** — Makes OJS client accessible via `HttpContext` extensions
- **`MapOjsWebhook()`** — Maps a webhook endpoint for receiving push-delivered jobs

## Configuration

### From `appsettings.json`

```json
{
  "Ojs": {
    "BaseUrl": "http://localhost:8080",
    "AuthToken": "your-token-here",
    "Worker": {
      "Queues": ["default", "emails"],
      "Concurrency": 10,
      "PollIntervalSeconds": 2.0,
      "ShutdownTimeoutSeconds": 25.0
    }
  }
}
```

```csharp
builder.Services.AddOjs(builder.Configuration.GetSection("Ojs"));
```

### From environment variables

| Variable | Description | Default |
|----------|-------------|---------|
| `OJS_URL` | Backend URL | `http://localhost:8080` |
| `OJS_AUTH_TOKEN` | Bearer token | none |
| `OJS_QUEUES` | Comma-separated queues | `default` |
| `OJS_CONCURRENCY` | Worker concurrency | `10` |

## Job Handlers

Implement the `IOjsJobHandler` interface:

```csharp
public class EmailSendHandler : IOjsJobHandler
{
    private readonly IEmailService _emailService;

    public EmailSendHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public async Task HandleAsync(JobContext context)
    {
        var to = context.Job.Args[0]?.ToString();
        var subject = context.Job.Args[1]?.ToString();
        await _emailService.SendAsync(to!, subject!);
    }
}

// Register in DI
builder.Services.AddOjsHandler<EmailSendHandler>("email.send");
```

## Middleware

Use `UseOjs()` to make the OJS client available via `HttpContext`:

```csharp
var app = builder.Build();
app.UseOjs();

app.MapPost("/api/send-email", async (HttpContext ctx) =>
{
    var client = ctx.GetOjsClient();
    await client.EnqueueAsync("email.send", new[] { "user@example.com", "Welcome!" });
    return Results.Ok(new { status = "queued" });
});
```

## Webhook Endpoint

Map a webhook endpoint for push-based job delivery from an OJS backend:

```csharp
var app = builder.Build();
app.MapOjsWebhook("/ojs/webhook");
```

The webhook endpoint accepts POST requests with a job payload, dispatches to the matching registered handler, and sends ack/nack callbacks to the OJS backend.

## License

Apache License 2.0 — see [LICENSE](../LICENSE) for details.

---

## OpenJobSpec.WorkerService

.NET Worker Service integration for running OJS workers as standalone background services without ASP.NET Core. Ideal for dedicated worker processes, microservices, and containerized deployments.

### Installation

```bash
dotnet add package OpenJobSpec.WorkerService
```

### Quick Start

```csharp
using OpenJobSpec.WorkerService;

var builder = Host.CreateApplicationBuilder(args);

builder.AddOjsWorker(options =>
{
    options.BaseUrl = "http://localhost:8080";
    options.Queues = ["emails", "notifications"];
    options.Concurrency = 10;
});

// Register job handlers
builder.Services.AddOjsJobHandler<EmailSendHandler>("email.send");
builder.Services.AddOjsJobHandler<NotificationHandler>("notification.push");

var host = builder.Build();
host.Run();
```

### Features

- **`AddOjsWorker()`** — Registers client, worker, and background service with the Generic Host
- **`AddOjsJobHandler<T>()`** — Registers typed job handlers resolved from DI
- **BackgroundService** — Worker runs as a proper `BackgroundService` with graceful shutdown
- **Health checks** — Optional health check for OJS backend connectivity (enabled by default)
- **Configuration binding** — Bind from `appsettings.json` or environment variables
- **IHostBuilder support** — Works with both `HostApplicationBuilder` and `IHostBuilder`

### Configuration

#### From `appsettings.json`

```json
{
  "Ojs": {
    "BaseUrl": "http://localhost:8080",
    "AuthToken": "your-token-here",
    "Queues": ["default", "emails"],
    "Concurrency": 10,
    "PollIntervalSeconds": 2.0,
    "ShutdownTimeoutSeconds": 25.0,
    "EnableHealthCheck": true
  }
}
```

```csharp
builder.AddOjsWorker(builder.Configuration.GetSection("Ojs"));
```

#### From environment variables

| Variable | Description | Default |
|----------|-------------|---------|
| `OJS_URL` | Backend URL | `http://localhost:8080` |
| `OJS_AUTH_TOKEN` | Bearer token | none |
| `OJS_QUEUES` | Comma-separated queues | `default` |
| `OJS_CONCURRENCY` | Worker concurrency | `10` |

### Job Handlers

Implement the `IOjsJobHandler` interface:

```csharp
public class EmailSendHandler : IOjsJobHandler
{
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailSendHandler> _logger;

    public EmailSendHandler(IEmailService emailService, ILogger<EmailSendHandler> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public async Task HandleAsync(JobContext context)
    {
        var to = context.Job.Args[0]?.ToString();
        _logger.LogInformation("Sending email to {Recipient}", to);
        await _emailService.SendAsync(to!);
    }
}
```

### Using IHostBuilder (older pattern)

```csharp
await Host.CreateDefaultBuilder(args)
    .AddOjsWorker(options =>
    {
        options.BaseUrl = "http://localhost:8080";
        options.Queues = ["default"];
    })
    .ConfigureServices(services =>
    {
        services.AddOjsJobHandler<EmailSendHandler>("email.send");
    })
    .Build()
    .RunAsync();
```

### License

Apache License 2.0 — see [LICENSE](../LICENSE) for details.

