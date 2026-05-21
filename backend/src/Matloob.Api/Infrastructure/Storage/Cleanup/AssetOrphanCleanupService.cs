using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Matloob.Api.Infrastructure.Storage.Cleanup;

/// <summary>
/// One-pass implementation of the orphan-cleanup job referenced by every
/// soft-delete path in the Assets API. Spec §11: file bytes survive on
/// disk for <c>Storage:SoftDeletedRetentionDays</c> days after their DB
/// row is soft-deleted, giving operators a window to restore the pair if
/// the delete was a mistake. After that window passes, the bytes are
/// physically removed; the DB row stays in place as audit.
///
/// This class is the work-doing core. The
/// <see cref="AssetOrphanCleanupBackgroundService"/> drives it on a
/// schedule; tests call <see cref="CleanupAsync"/> directly.
///
/// Properties of a single pass:
/// - Idempotent. Re-running is safe: rows whose file is already gone
///   come back as NotFound and are counted, not failed.
/// - Batched. <see cref="FileStorageOptions.CleanupBatchSize"/> rows per
///   pass keeps memory and filesystem-call counts bounded.
/// - Fault-tolerant. A single file's IO error is logged and the loop
///   continues with the next row.
/// - Read-only on the DB. We never delete or mutate DB rows here; the
///   row is the audit trail that the bytes once existed.
/// - Driver-aware. Only assets with <c>StorageDriver = Local</c> are
///   processed today; rows with any future driver value are skipped
///   with a debug log entry.
/// </summary>
public sealed class AssetOrphanCleanupService
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly FileStorageOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<AssetOrphanCleanupService> _logger;

    public AssetOrphanCleanupService(
        AppDbContext db,
        IFileStorage storage,
        IOptions<FileStorageOptions> options,
        TimeProvider clock,
        ILogger<AssetOrphanCleanupService> logger)
    {
        _db = db;
        _storage = storage;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Run one cleanup pass: pull up to <c>CleanupBatchSize</c> candidate
    /// rows whose <c>DeletedAt</c> is older than the retention window,
    /// and delete each one's file. Returns a summary the caller (or test)
    /// can log + assert on.
    /// </summary>
    public async Task<CleanupSummary> CleanupAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var cutoff = now - TimeSpan.FromDays(_options.SoftDeletedRetentionDays);

        // Candidates: soft-deleted, past the retention window, local-driver.
        // IgnoreQueryFilters() is required because the global ISoftDeletable
        // filter would hide IsDeleted = true rows -- that's exactly what we
        // need to see here.
        var candidates = await _db.Assets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.IsDeleted
                     && a.DeletedAt != null
                     && a.DeletedAt <= cutoff
                     && a.StorageDriver == AssetStorageDriver.Local
                     && a.RelativePath != null
                     && a.RelativePath != "")
            .OrderBy(a => a.DeletedAt)
            .Take(_options.CleanupBatchSize)
            .Select(a => new CleanupCandidate(a.Id, a.RelativePath, a.StorageDriver))
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            _logger.LogDebug(
                "Asset orphan cleanup found nothing to do (cutoff = {Cutoff:o}).",
                cutoff);
            return CleanupSummary.Empty;
        }

        int deleted = 0, alreadyGone = 0, refused = 0, errored = 0;

        foreach (var candidate in candidates)
        {
            if (ct.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Asset orphan cleanup cancelled mid-batch after processing {Processed}/{Total} rows.",
                    deleted + alreadyGone + refused + errored,
                    candidates.Count);
                break;
            }

            try
            {
                var result = await _storage.DeleteAsync(candidate.RelativePath!, ct);
                switch (result)
                {
                    case DeleteResult.Deleted:
                        deleted++;
                        break;
                    case DeleteResult.NotFound:
                        alreadyGone++;
                        _logger.LogDebug(
                            "Asset orphan cleanup: file already missing for asset {AssetId} at {RelativePath}.",
                            candidate.Id, candidate.RelativePath);
                        break;
                    case DeleteResult.Refused:
                        refused++;
                        _logger.LogWarning(
                            "Asset orphan cleanup refused path for asset {AssetId} (relative_path = {RelativePath}). " +
                            "This usually means a corrupted DB row.",
                            candidate.Id, candidate.RelativePath);
                        break;
                }
            }
            catch (Exception ex)
            {
                // Per-asset failure must not abort the pass. Log + continue;
                // the next pass will retry this row.
                errored++;
                _logger.LogError(ex,
                    "Asset orphan cleanup failed to delete file for asset {AssetId} at {RelativePath}. " +
                    "Continuing with the next row; the next pass will retry.",
                    candidate.Id, candidate.RelativePath);
            }
        }

        var summary = new CleanupSummary(
            Examined: candidates.Count,
            Deleted: deleted,
            AlreadyGone: alreadyGone,
            Refused: refused,
            Errored: errored,
            Cutoff: cutoff);

        _logger.LogInformation(
            "Asset orphan cleanup pass complete: examined={Examined} deleted={Deleted} " +
            "alreadyGone={AlreadyGone} refused={Refused} errored={Errored} cutoff={Cutoff:o}.",
            summary.Examined, summary.Deleted, summary.AlreadyGone, summary.Refused,
            summary.Errored, summary.Cutoff);

        return summary;
    }

    private sealed record CleanupCandidate(Guid Id, string? RelativePath, AssetStorageDriver StorageDriver);
}

/// <summary>
/// Numeric breakdown of one cleanup pass. Useful for log assertions in
/// integration tests and for ops dashboards.
/// </summary>
public sealed record CleanupSummary(
    int Examined,
    int Deleted,
    int AlreadyGone,
    int Refused,
    int Errored,
    DateTimeOffset Cutoff)
{
    public static readonly CleanupSummary Empty =
        new(Examined: 0, Deleted: 0, AlreadyGone: 0, Refused: 0, Errored: 0,
            Cutoff: DateTimeOffset.MinValue);
}
