using Matloob.Api.Infrastructure.Events.Dispatcher;
using Matloob.Api.Infrastructure.Notifications;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Applications;
using Matloob.Domain.Events;
using Matloob.Domain.Notifications;
using Matloob.Domain.Offers;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Notifications.Fanout;

/// <summary>
/// Outbox subscriber that turns offer-lifecycle events into notifications,
/// mirroring the legacy Laravel notification fanout (the subset whose
/// triggering events are emitted today):
/// <list type="bullet">
///   <item><c>offer.accepted</c> / <c>offer.rejected</c> → a database
///   notification to the offer's <b>sender establishment</b> (legacy
///   <c>OfferAccepted/RejectedNotification</c>, <c>sent_offer</c>).</item>
///   <item><c>offer.created</c> → the applicant's new-offer alert, which had no
///   database channel — delivered over the (no-op) SMS channel so it isn't
///   silently dropped (legacy <c>NewOfferReceivedNotification</c>).</item>
/// </list>
/// Other event types are ignored. Notifications are staged on the shared
/// AppDbContext and committed by the dispatcher.
/// </summary>
public sealed class NotificationOutboxHandler : IOutboxHandler
{
    private readonly AppDbContext _db;
    private readonly ISmsSender _sms;

    public NotificationOutboxHandler(AppDbContext db, ISmsSender sms)
    {
        _db = db;
        _sms = sms;
    }

    public async Task HandleAsync(OutboxEvent evt, CancellationToken ct)
    {
        switch (evt.EventType)
        {
            case OfferEventTypes.Accepted:
                await AddOfferOutcomeAsync(evt.AggregateId, "Accepted your offer.", ct);
                break;
            case OfferEventTypes.Rejected:
                await AddOfferOutcomeAsync(evt.AggregateId, "Rejected your offer.", ct);
                break;
            case OfferEventTypes.Created:
                await SendNewOfferSmsAsync(evt.AggregateId, ct);
                break;
            case OfferEventTypes.IsActive:
                await AddOfferIsActiveAsync(evt.AggregateId, ct);
                break;
            case EventEventTypes.Started:
                await AddEventNotificationAsync(evt.AggregateId, "Your event has started today", ct);
                break;
            case EventEventTypes.Finished:
                await AddEventNotificationAsync(evt.AggregateId, "Your event has ended today", ct);
                break;
            case OpportunityEventTypes.Expired:
                await AddOpportunityFanoutAsync(evt.AggregateId, onlyApplicantsWithoutActiveOffer: false, ct);
                break;
            case OpportunityEventTypes.Fulfilled:
                await AddOpportunityFanoutAsync(evt.AggregateId, onlyApplicantsWithoutActiveOffer: true, ct);
                break;
        }
    }

    private async Task AddOfferOutcomeAsync(Guid offerId, string message, CancellationToken ct)
    {
        var offer = await _db.Offers.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null) return;

        var applicant = await ResolveApplicantAsync(offer.ApplicationId, ct);

        _db.Notifications.Add(new Notification(
            Guid.NewGuid(),
            NotificationRecipientType.Establishment,
            offer.SenderEstablishmentId.ToString(),
            NotificationTypes.SentOffer,
            title: applicant.Name ?? "Applicant",
            message: message,
            resourceType: "offers",
            resourceId: offer.Id.ToString()));
    }

    private async Task SendNewOfferSmsAsync(Guid offerId, CancellationToken ct)
    {
        var offer = await _db.Offers.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null) return;

        var applicant = await ResolveApplicantAsync(offer.ApplicationId, ct);
        if (applicant.RecipientId is null) return;

        await _sms.SendAsync(
            applicant.RecipientId,
            "You have received a new offer. Log in to view the details.",
            ct);
    }

    // -- FYI fanout (time-driven status sync + accept-time) -------------------

    /// <summary>
    /// <c>offer.is_active</c> → tell the APPLICANT their offer is now active
    /// (legacy <c>OfferIsActiveNotification</c>, <c>received_offer</c>).
    /// </summary>
    private async Task AddOfferIsActiveAsync(Guid offerId, CancellationToken ct)
    {
        var offer = await _db.Offers.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null) return;

        var app = await _db.OpportunityApplications.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == offer.ApplicationId, ct);
        if (app is null) return;

        var (type, id) = ResolveApplicantRecipient(app);
        if (type is null || id is null) return;

        var senderName = await _db.Establishments.AsNoTracking()
            .Where(e => e.Id == offer.SenderEstablishmentId)
            .Select(e => e.Name).FirstOrDefaultAsync(ct);

        _db.Notifications.Add(new Notification(
            Guid.NewGuid(),
            type.Value,
            id,
            NotificationTypes.ReceivedOffer,
            title: "Contract is Active",
            message: $"Congratulations! Your contract is now active with {senderName ?? "the establishment"}",
            resourceType: "offers",
            resourceId: offer.Id.ToString()));
    }

    /// <summary>
    /// <c>event.started</c> / <c>event.finished</c> → tell the event's
    /// establishment (legacy <c>EventStarted/EventEndedNotification</c>).
    /// </summary>
    private async Task AddEventNotificationAsync(Guid eventId, string message, CancellationToken ct)
    {
        var ev = await _db.Events.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return;

        _db.Notifications.Add(new Notification(
            Guid.NewGuid(),
            NotificationRecipientType.Establishment,
            ev.EstablishmentId.ToString(),
            NotificationTypes.Event,
            title: ev.Name,
            message: message,
            resourceType: "events",
            resourceId: ev.Id.ToString()));
    }

    /// <summary>
    /// <c>opportunity.expired</c> / <c>opportunity.fulfilled</c> → tell the
    /// opportunity's applicants it closed (legacy
    /// <c>OpportunityExpired/FulfilledNotification</c>). For the fulfilled
    /// case, applicants who already hold an active offer (the hired ones) are
    /// excluded — matching legacy <c>whereDoesntHave('offer', activeStatuses)</c>.
    /// </summary>
    private async Task AddOpportunityFanoutAsync(
        Guid opportunityId, bool onlyApplicantsWithoutActiveOffer, CancellationToken ct)
    {
        var opp = await _db.Opportunities.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == opportunityId, ct);
        if (opp is null) return;

        const string message =
            "The opportunity you applied for has either expired or filled. "
            + "Please explore new opportunities available on the platform";

        var applications = await _db.OpportunityApplications.AsNoTracking()
            .Where(a => a.OpportunityId == opportunityId)
            .ToListAsync(ct);

        foreach (var app in applications)
        {
            if (onlyApplicantsWithoutActiveOffer)
            {
                var holdsActiveOffer = await _db.Offers.AsNoTracking()
                    .AnyAsync(o => o.ApplicationId == app.Id
                                && OfferStatusSets.Active.Contains(o.Status), ct);
                if (holdsActiveOffer) continue;
            }

            var (type, id) = ResolveApplicantRecipient(app);
            if (type is null || id is null) continue;

            _db.Notifications.Add(new Notification(
                Guid.NewGuid(),
                type.Value,
                id,
                NotificationTypes.Opportunity,
                title: opp.Name,
                message: message,
                resourceType: "opportunities",
                resourceId: opp.Id.ToString()));
        }
    }

    /// <summary>
    /// Maps an application's applier FK to a notification recipient
    /// (type + id), without a DB round-trip. Exactly one FK is set.
    /// </summary>
    private static (NotificationRecipientType? Type, string? Id) ResolveApplicantRecipient(
        OpportunityApplication app)
    {
        if (app.ApplicantEstablishmentId is { } estId)
            return (NotificationRecipientType.Establishment, estId.ToString());
        if (!string.IsNullOrEmpty(app.ApplicantUserId))
            return (NotificationRecipientType.User, app.ApplicantUserId);
        return (null, null);
    }

    private async Task<(string? RecipientId, string? Name)> ResolveApplicantAsync(
        Guid applicationId, CancellationToken ct)
    {
        var app = await _db.OpportunityApplications.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (app is null) return (null, null);

        if (app.ApplicantEstablishmentId is { } estId)
        {
            var name = await _db.Establishments.AsNoTracking()
                .Where(e => e.Id == estId).Select(e => e.Name).FirstOrDefaultAsync(ct);
            return (estId.ToString(), name);
        }

        if (!string.IsNullOrEmpty(app.ApplicantUserId))
        {
            var name = await _db.Users.AsNoTracking()
                .Where(u => u.IdentityId == app.ApplicantUserId).Select(u => u.Name)
                .FirstOrDefaultAsync(ct);
            return (app.ApplicantUserId, name);
        }

        return (null, null);
    }
}
