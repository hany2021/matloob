using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Matloob.Api.Infrastructure.Events.Dispatcher;

/// <summary>
/// Hosted background service that drives
/// <see cref="OutboxDispatcherService.DispatchAsync"/> on a schedule.
///
/// Lifecycle mirrors the asset-cleanup background service:
/// - Boots with the API host. If <see cref="OutboxOptions.DispatcherEnabled"/>
///   is <c>false</c> (the default), the loop exits immediately and never
///   touches the DB.
/// - Runs one pass IMMEDIATELY on startup so a crash-restart cycle doesn't
///   leave events accumulating for a full interval.
/// - Sleeps <see cref="OutboxOptions.IntervalSeconds"/> seconds between
///   passes via PeriodicTimer; respects host shutdown via the
///   ExecuteAsync cancellation token.
/// - Each iteration creates a fresh DI scope for AppDbContext + the
///   dispatcher service.
/// - Pass-level exceptions are caught + logged. Host never crashes from
///   dispatcher failures.
/// </summary>
internal sealed class OutboxDispatcherBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxDispatcherBackgroundService> _logger;

    public OutboxDispatcherBackgroundService(
        IServiceProvider services,
        IOptions<OutboxOptions> options,
        ILogger<OutboxDispatcherBackgroundService> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.DispatcherEnabled)
        {
            _logger.LogInformation(
                "Outbox dispatcher is disabled (Outbox:DispatcherEnabled=false). " +
                "Service will idle until the host stops.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.IntervalSeconds));
        _logger.LogInformation(
            "Outbox dispatcher background service started. " +
            "Interval={IntervalSeconds}s, BatchSize={BatchSize}, MaxAttempts={MaxAttempts}.",
            _options.IntervalSeconds, _options.BatchSize, _options.MaxAttempts);

        await RunOnePassSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnePassSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown -- normal exit.
        }

        _logger.LogInformation("Outbox dispatcher background service stopped.");
    }

    private async Task RunOnePassSafelyAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var dispatcher = scope.ServiceProvider
                .GetRequiredService<OutboxDispatcherService>();
            await dispatcher.DispatchAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Pass-level failure must not crash the host. Log + continue.
            _logger.LogError(ex,
                "Outbox dispatcher pass failed; will retry on the next interval.");
        }
    }
}
