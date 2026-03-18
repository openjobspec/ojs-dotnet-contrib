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
        _logger.LogInformation("OJS Worker Service initiating graceful shutdown…");

        // Phase 1: Call pre-shutdown hooks
        foreach (var hook in _options.PreShutdownHooks)
        {
            try
            {
                await hook(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pre-shutdown hook failed");
            }
        }

        // Phase 2: Stop worker and drain active jobs
        _logger.LogInformation(
            "Draining {ActiveJobs} active jobs (timeout: {Timeout}s)…",
            _worker.ActiveJobCount, _options.ShutdownTimeoutSeconds);

        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_options.ShutdownTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        var timedOut = false;
        try
        {
            var stopTask = _worker.StopAsync(linkedCts.Token);

            // Log shutdown progress at intervals
            while (!stopTask.IsCompleted)
            {
                if (await Task.WhenAny(stopTask, Task.Delay(5000)) != stopTask
                    && _worker.ActiveJobCount > 0)
                {
                    _logger.LogInformation(
                        "Shutdown in progress: {ActiveJobs} active jobs remaining, {Processed} total processed",
                        _worker.ActiveJobCount, _worker.ProcessedCount);
                }
            }

            await stopTask;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            timedOut = true;
            _logger.LogWarning(
                "Shutdown timeout reached ({Timeout}s) with {ActiveJobs} jobs still in-flight",
                _options.ShutdownTimeoutSeconds, _worker.ActiveJobCount);
        }

        // Phase 3: Final status
        if (!timedOut && _worker.ActiveJobCount == 0)
        {
            _logger.LogInformation(
                "OJS Worker Service stopped gracefully ({Processed} total jobs processed)",
                _worker.ProcessedCount);
        }
        else if (_worker.ActiveJobCount > 0)
        {
            _logger.LogWarning(
                "Forced shutdown completed with {ActiveJobs} jobs still in-flight",
                _worker.ActiveJobCount);
        }

        await base.StopAsync(cancellationToken);
    }
}
