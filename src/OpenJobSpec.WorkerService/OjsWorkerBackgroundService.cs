using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenJobSpec;

namespace OpenJobSpec.WorkerService;

/// <summary>
/// Hosted service that runs the OJS worker as a background service.
/// Integrates with the .NET Generic Host lifecycle for graceful startup and shutdown.
/// </summary>
internal sealed class OjsWorkerBackgroundService : BackgroundService
{
    private readonly OJSWorker _worker;
    private readonly OjsWorkerServiceOptions _options;
    private readonly ILogger<OjsWorkerBackgroundService> _logger;

    public OjsWorkerBackgroundService(
        OJSWorker worker,
        OjsWorkerServiceOptions options,
        ILogger<OjsWorkerBackgroundService> logger)
    {
        _worker = worker;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OJS Worker Service starting (queues: [{Queues}], concurrency: {Concurrency})",
            string.Join(", ", _options.Queues),
            _options.Concurrency);

        try
        {
            await _worker.StartAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("OJS Worker Service received shutdown signal");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OJS Worker Service encountered an error");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("OJS Worker Service stopping, draining active jobs…");
        await _worker.StopAsync();
        _logger.LogInformation("OJS Worker Service stopped");
        await base.StopAsync(cancellationToken);
    }
}
