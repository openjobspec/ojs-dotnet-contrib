# OJS .NET Contrib — ASP.NET Core Integration

[![NuGet](https://img.shields.io/nuget/v/OpenJobSpec.AspNetCore)](https://www.nuget.org/packages/OpenJobSpec.AspNetCore)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)

ASP.NET Core integration for the [Open Job Spec](https://openjobspec.org) .NET SDK. Provides dependency injection, health checks, and hosted service integration for seamless background job processing in ASP.NET Core applications.

## Installation

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

## License

Apache License 2.0 — see [LICENSE](../LICENSE) for details.
