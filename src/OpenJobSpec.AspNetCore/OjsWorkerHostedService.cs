using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenJobSpec;

namespace OpenJobSpec.AspNetCore;

/// <summary>
/// Hosted service that wraps the OJS worker lifecycle with ASP.NET Core.
/// Starts the worker when the application starts and gracefully stops it on shutdown.
/// </summary>
internal sealed class OjsWorkerHostedService : IHostedService
{
    private readonly OJSWorker _worker;
    private readonly OjsOptions _options;
    private readonly ILogger<OjsWorkerHostedService> _logger;

    public OjsWorkerHostedService(OJSWorker worker, OjsOptions options, ILogger<OjsWorkerHostedService> logger)
    {
        _worker = worker;
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "OJS Worker starting (queues: {Queues}, concurrency: {Concurrency})",
            string.Join(", ", _options.Worker.Queues),
            _options.Worker.Concurrency
        );

        _ = Task.Run(() => _worker.StartAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("OJS Worker stopping, draining active jobs…");
        await _worker.StopAsync();
        _logger.LogInformation("OJS Worker stopped");
    }
}
