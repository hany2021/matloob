using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Matloob.Api.Infrastructure.Storage.Cleanup;

/// <summary>
/// Hosted background service that runs
/// <see cref="AssetOrphanCleanupService.CleanupAsync"/> on a schedule.
///
/// Lifecycle:
/// - Boots with the API host. If <see cref="FileStorageOptions.CleanupEnabled"/>
///   is <c>false</c> (the default) the loop exits immediately and never
///   touches the DB — Dev hosts stay quiet without an explicit opt-in.
/// - Each iteration creates a fresh DI scope so <see cref="AssetOrphanCleanupService"/>
///   resolves a per-pass <c>AppDbContext</c> rather than reusing one for
///   the whole process lifetime.
/// - Sleeps <see cref="FileStorageOptions.CleanupIntervalMinutes"/> minutes
///   between passes. The PeriodicTimer respects host shutdown — when
///   ExecuteAsync's CancellationToken is signalled, the timer's
///   WaitForNextTickAsync returns false and the loop exits cleanly.
/// - Exceptions inside a single pass are caught + logged. The loop does
///   NOT crash the host; the orphan backlog will be retried on the next
///   tick.
/// </summary>
internal sealed class AssetOrphanCleanupBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly FileStorageOptions _options;
    private readonly ILogger<AssetOrphanCleanupBackgroundService> _logger;

    public AssetOrphanCleanupBackgroundService(
        IServiceProvider services,
        IOptions<FileStorageOptions> options,
        ILogger<AssetOrphanCleanupBackgroundService> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CleanupEnabled)
        {
            _logger.LogInformation(
                "Asset orphan cleanup is disabled (Storage:CleanupEnabled=false). " +
                "Service will idle until the host stops.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.CleanupIntervalMinutes));
        _logger.LogInformation(
            "Asset orphan cleanup background service started. " +
            "Interval={IntervalMinutes}m, RetentionDays={RetentionDays}, BatchSize={BatchSize}.",
            _options.CleanupIntervalMinutes,
            _options.SoftDeletedRetentionDays,
            _options.CleanupBatchSize);

        // First pass runs immediately on startup so a crash-then-restart
        // doesn't leave the backlog growing for a full interval.
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

        _logger.LogInformation("Asset orphan cleanup background service stopped.");
    }

    private async Task RunOnePassSafelyAsync(CancellationToken ct)
    {
        try
        {
            // A fresh scope per pass keeps DbContext + interceptor instances
            // from leaking between iterations (the host owns one root SP for
            // the whole process; BackgroundService.ExecuteAsync runs in that
            // root scope by default).
            using var scope = _services.CreateScope();
            var cleanup = scope.ServiceProvider
                .GetRequiredService<AssetOrphanCleanupService>();
            await cleanup.CleanupAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Bubble; the outer loop's catch handles it.
            throw;
        }
        catch (Exception ex)
        {
            // A pass-level failure (e.g. transient DB connection drop) must
            // not crash the host. Log + continue; the next tick retries.
            _logger.LogError(ex,
                "Asset orphan cleanup pass failed; will retry on the next interval.");
        }
    }
}
