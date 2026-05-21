using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Matloob.Api.Infrastructure.Events.Dispatcher;

/// <summary>
/// One-pass implementation of the outbox dispatcher. Pulls up to
/// <see cref="OutboxOptions.BatchSize"/> unprocessed rows ordered by
/// <see cref="OutboxEvent.OccurredAt"/>, hands each to the registered
/// in-process handlers (today: just marks them processed — there are no
/// real subscribers yet), and stamps the result back onto the row.
///
/// The hosted <see cref="OutboxDispatcherBackgroundService"/> drives this
/// on a schedule; tests call <see cref="DispatchAsync"/> directly to assert
/// per-pass outcomes.
///
/// Pass behavior:
/// - Idempotent at the row level (the WHERE clause filters out already-
///   processed rows; double-running a pass is a no-op for rows it already
///   handled).
/// - Fault-tolerant per row: a single handler exception is logged and
///   recorded on the row (<see cref="OutboxEvent.MarkFailed"/>) without
///   aborting the pass.
/// - Bounded retries: a row whose <see cref="OutboxEvent.Attempts"/> hits
///   <see cref="OutboxOptions.MaxAttempts"/> stops being picked up. The
///   row stays in the table with Error / Attempts populated so ops can
///   triage it.
/// </summary>
public sealed class OutboxDispatcherService
{
    private readonly AppDbContext _db;
    private readonly OutboxOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutboxDispatcherService> _logger;

    public OutboxDispatcherService(
        AppDbContext db,
        IOptions<OutboxOptions> options,
        TimeProvider clock,
        ILogger<OutboxDispatcherService> logger)
    {
        _db = db;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Run one dispatch pass. Returns counters for log assertion + ops use.
    /// </summary>
    public async Task<DispatchSummary> DispatchAsync(CancellationToken ct)
    {
        var batchSize = Math.Max(1, _options.BatchSize);
        var maxAttempts = Math.Max(1, _options.MaxAttempts);

        // Pull a batch of pending rows. Rows that have exhausted their
        // retry budget are excluded; an operator can reset Attempts to
        // re-include them.
        var rows = await _db.OutboxEvents
            .Where(e => e.ProcessedAt == null && e.Attempts < maxAttempts)
            .OrderBy(e => e.OccurredAt)
            .Take(batchSize)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return DispatchSummary.Empty;
        }

        int processed = 0, failed = 0;

        foreach (var row in rows)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // No real subscribers yet -- the dispatcher's job today is
                // to drain the table. When a future commit adds in-process
                // handlers, dispatch them here (e.g. via a registered
                // collection of IOutboxHandler<TPayload>).
                DispatchHandlerNoOp(row);

                row.MarkProcessed(_clock.GetUtcNow());
                processed++;
            }
            catch (Exception ex)
            {
                // Single-row failure must not abort the pass.
                row.MarkFailed(ex.Message);
                failed++;
                _logger.LogError(ex,
                    "Outbox dispatch failed for event {EventId} ({EventType} on {AggregateType} {AggregateId}). " +
                    "Attempts now {Attempts}.",
                    row.Id, row.EventType, row.AggregateType, row.AggregateId, row.Attempts);
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Outbox dispatch pass complete: processed={Processed} failed={Failed} examined={Examined}.",
            processed, failed, rows.Count);

        return new DispatchSummary(rows.Count, processed, failed);
    }

    /// <summary>
    /// Placeholder for the in-process handler dispatch. No-op today --
    /// outbox rows are simply marked processed. When real subscribers
    /// land (notifications, audit feed, etc.), they hook in here.
    /// </summary>
    private static void DispatchHandlerNoOp(OutboxEvent _) { }
}

/// <summary>Counters for one dispatch pass.</summary>
public sealed record DispatchSummary(int Examined, int Processed, int Failed)
{
    public static readonly DispatchSummary Empty = new(0, 0, 0);
}
