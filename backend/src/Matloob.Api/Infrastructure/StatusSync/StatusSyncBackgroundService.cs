using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Matloob.Api.Infrastructure.StatusSync;

/// <summary>
/// Hosted service that runs <see cref="StatusSyncService.RunAsync"/> on a timer.
/// Same lifecycle shape as <c>AssetOrphanCleanupBackgroundService</c>:
/// opt-in via <see cref="StatusSyncOptions.Enabled"/>, a fresh DI scope per pass
/// (fresh <c>AppDbContext</c>), pass-level exceptions caught + logged so a
/// transient failure never crashes the host, and a clean exit on shutdown.
/// </summary>
internal sealed class StatusSyncBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly StatusSyncOptions _options;
    private readonly ILogger<StatusSyncBackgroundService> _logger;

    public StatusSyncBackgroundService(
        IServiceProvider services,
        IOptions<StatusSyncOptions> options,
        ILogger<StatusSyncBackgroundService> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Status sync is disabled (StatusSync:Enabled=false). Service will idle until the host stops.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
        _logger.LogInformation(
            "Status sync background service started. Interval={IntervalMinutes}m.", _options.IntervalMinutes);

        // First pass immediately so a restart doesn't wait a full interval.
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

        _logger.LogInformation("Status sync background service stopped.");
    }

    private async Task RunOnePassSafelyAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<StatusSyncService>();
            await sync.RunAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Status sync pass failed; will retry on the next interval.");
        }
    }
}
