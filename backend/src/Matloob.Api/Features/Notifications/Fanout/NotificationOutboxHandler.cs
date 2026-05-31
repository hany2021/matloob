using Matloob.Api.Infrastructure.Events.Dispatcher;
using Matloob.Api.Infrastructure.Notifications;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Events;
using Matloob.Domain.Notifications;
using Matloob.Domain.Offers;
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
