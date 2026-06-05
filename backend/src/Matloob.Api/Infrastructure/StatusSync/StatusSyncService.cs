using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Events;
using Matloob.Domain.Offers;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Matloob.Api.Infrastructure.StatusSync;

/// <summary>
/// One-pass time-driven status reconciliation — the new-system equivalent of the
/// legacy <c>SyncEventsStatuses</c> + <c>SyncOffersStatuses</c> scheduled
/// commands. Some statuses are derived from dates, not from any user action, so
/// nothing in a request flips them when the date rolls over. This sweep pulls
/// them forward:
///
/// <list type="bullet">
///   <item>Event: Upcoming + start ≤ today → Active; Active + end &lt; today → Finished.</item>
///   <item>Opportunity: Upcoming + start ≤ today → Active; Upcoming/Active + end &lt; today → Finished.</item>
///   <item>Offer: Pending/PendingSponsorApproval + validity expired → Expired;
///         Accepted + job end &lt; today → WaitingForEvaluation (unblocks evaluation).</item>
/// </list>
///
/// <para>
/// Each rule is a one-way status move, so the sweep is naturally idempotent —
/// a row only matches while it is in the "before" status, and the transition
/// moves it out of the matched set. Re-running changes nothing.
/// </para>
///
/// <para>
/// This is the work-doing core; <see cref="StatusSyncBackgroundService"/> drives
/// it on a timer and tests call <see cref="RunAsync"/> directly with a pinned
/// <see cref="TimeProvider"/>.
/// </para>
/// </summary>
public sealed class StatusSyncService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<StatusSyncService> _logger;
    private readonly IOutboxWriter _outbox;

    public StatusSyncService(
        AppDbContext db, TimeProvider clock, ILogger<StatusSyncService> logger, IOutboxWriter outbox)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
        _outbox = outbox;
    }

    public async Task<StatusSyncSummary> RunAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var summary = new StatusSyncSummary();

        // -- Events ----------------------------------------------------------
        // Each transition emits the matching FYI outbox event so the
        // notification fanout (NotificationOutboxHandler) can tell the event's
        // establishment — legacy EventStarted/EventEndedNotification.
        var eventsToStart = await _db.Events
            .Where(e => e.Status == EventStatus.Upcoming && e.StartDate != null && e.StartDate <= today)
            .ToListAsync(ct);
        foreach (var e in eventsToStart)
        {
            e.MarkStarted(); summary.EventsStarted++;
            _outbox.Enqueue(EventEventTypes.Started, nameof(Event), e.Id, new { id = e.Id, at = now });
        }

        var eventsToFinish = await _db.Events
            .Where(e => e.Status == EventStatus.Active && e.EndDate != null && e.EndDate < today)
            .ToListAsync(ct);
        foreach (var e in eventsToFinish)
        {
            e.MarkFinished(); summary.EventsFinished++;
            _outbox.Enqueue(EventEventTypes.Finished, nameof(Event), e.Id, new { id = e.Id, at = now });
        }

        // -- Opportunities ---------------------------------------------------
        var oppsToStart = await _db.Opportunities
            .Where(o => o.Status == OpportunityStatus.Upcoming && o.StartDate <= today)
            .ToListAsync(ct);
        foreach (var o in oppsToStart) { o.MarkStarted(); summary.OpportunitiesStarted++; }

        var oppsToFinish = await _db.Opportunities
            .Where(o => (o.Status == OpportunityStatus.Upcoming || o.Status == OpportunityStatus.Active)
                        && o.EndDate < today)
            .ToListAsync(ct);
        foreach (var o in oppsToFinish)
        {
            o.MarkFinished(); summary.OpportunitiesFinished++;
            // Legacy OpportunityExpiredNotification — tell the applicants the
            // opportunity they applied for closed. (Upcoming→Active start has
            // no legacy notification, so it's not emitted.)
            _outbox.Enqueue(OpportunityEventTypes.Expired, nameof(Opportunity), o.Id, new { id = o.Id, at = now });
        }

        // -- Offers ----------------------------------------------------------
        var offersToExpire = await _db.Offers
            .Where(o => (o.Status == OfferStatus.Pending || o.Status == OfferStatus.PendingSponsorApproval)
                        && o.OfferValidityTo != null && o.OfferValidityTo < now)
            .ToListAsync(ct);
        foreach (var o in offersToExpire) { o.MarkExpired(); summary.OffersExpired++; }

        var offersToEvaluate = await _db.Offers
            .Where(o => o.Status == OfferStatus.Accepted && o.EndDate != null && o.EndDate < today)
            .ToListAsync(ct);
        foreach (var o in offersToEvaluate) { o.MarkWaitingForEvaluation(); summary.OffersWaitingForEvaluation++; }

        if (summary.TotalChanged > 0)
        {
            // Materialize the staged FYI events as outbox rows so they commit
            // in the same transaction as the status changes.
            _outbox.Flush();
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Status sync pass changed {Total} rows: events started={EventsStarted} finished={EventsFinished}, "
                + "opportunities started={OpportunitiesStarted} finished={OpportunitiesFinished}, "
                + "offers expired={OffersExpired} waitingForEvaluation={OffersWaitingForEvaluation}. now={Now:o}.",
                summary.TotalChanged, summary.EventsStarted, summary.EventsFinished,
                summary.OpportunitiesStarted, summary.OpportunitiesFinished,
                summary.OffersExpired, summary.OffersWaitingForEvaluation, now);
        }
        else
        {
            _logger.LogDebug("Status sync pass found nothing to do. now={Now:o}.", now);
        }

        return summary;
    }
}

/// <summary>Per-pass counts; useful for log assertions + ops dashboards.</summary>
public sealed class StatusSyncSummary
{
    public int EventsStarted { get; set; }
    public int EventsFinished { get; set; }
    public int OpportunitiesStarted { get; set; }
    public int OpportunitiesFinished { get; set; }
    public int OffersExpired { get; set; }
    public int OffersWaitingForEvaluation { get; set; }

    public int TotalChanged =>
        EventsStarted + EventsFinished + OpportunitiesStarted + OpportunitiesFinished
        + OffersExpired + OffersWaitingForEvaluation;
}
