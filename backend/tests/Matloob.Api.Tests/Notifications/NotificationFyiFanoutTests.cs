using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Events.Dispatcher;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Common;
using Matloob.Api.Tests.Opportunities;
using Matloob.Domain.Events;
using Matloob.Domain.Notifications;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Notifications;

/// <summary>
/// FYI notification fanout — the thin layer over status-sync transitions +
/// accept-time events that emits the 5 legacy database notifications:
/// <list type="bullet">
///   <item><c>offer.is_active</c> → the applicant (accept-time).</item>
///   <item><c>opportunity.fulfilled</c> → unserved applicants when the last
///     slot fills (accept-time).</item>
///   <item><c>event.started</c> / <c>event.finished</c> → the event's
///     establishment (status sync).</item>
///   <item><c>opportunity.expired</c> → the opportunity's applicants (status
///     sync).</item>
/// </list>
/// Isolated from <see cref="NotificationsFanoutTests"/> via its own factory
/// instance (unique in-memory DB); assertions are scoped to each test's own
/// resource ids so intra-class seeding doesn't bleed across cases.
/// </summary>
public sealed class NotificationFyiFanoutTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;
    private Guid _establishment;

    public NotificationFyiFanoutTests(OpportunitiesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker2.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);
        _establishment = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-FYI-FANOUT");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task DrainOutboxAsync()
    {
        using var scope = _factory.CreateDbScope();
        await scope.ServiceProvider
            .GetRequiredService<OutboxDispatcherService>()
            .DispatchAsync(CancellationToken.None);
    }

    private async Task<Guid> SendOfferAsync(Guid applicationId)
    {
        var owner = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var send = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishment}/offers/send",
            new { applicant_id = applicationId, monthly_salary = 6000m });
        send.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await send.Content.ReadAsStringAsync());
        return doc.RootElement.DataOf().GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task OfferAccepted_NotifiesApplicantOfferIsActive()
    {
        var opp = await OaoHelpers.SeedOpportunityAsync(
            _factory, _establishment, name: "Active-Vacancy", requiredPersonnel: 10);
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, opp, applicantUserId: OaoHelpers.Worker.Sub);
        var offerId = await SendOfferAsync(appId);

        var worker = _factory.CreateClientFor(OaoHelpers.Worker);
        Assert.Equal(HttpStatusCode.OK,
            (await worker.PostAsync($"/api/users/offers/{offerId}/accept", content: null)).StatusCode);

        await DrainOutboxAsync();

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifs = await db.Notifications.Where(n =>
            n.RecipientType == NotificationRecipientType.User
            && n.RecipientId == OaoHelpers.Worker.Sub
            && n.ResourceId == offerId.ToString()
            && n.Type == NotificationTypes.ReceivedOffer).ToListAsync();
        Assert.Single(notifs);
        Assert.Equal("Contract is Active", notifs[0].Title);
    }

    [Fact]
    public async Task OfferAccepted_FillsLastSlot_NotifiesUnservedApplicantsOnly()
    {
        // requiredPersonnel = 1 → the single acceptance fills the opportunity.
        var opp = await OaoHelpers.SeedOpportunityAsync(
            _factory, _establishment, name: "OneSlot", requiredPersonnel: 1);
        var hiredApp = await OaoHelpers.SeedApplicationAsync(
            _factory, opp, applicantUserId: OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedApplicationAsync(
            _factory, opp, applicantUserId: OaoHelpers.Worker2.Sub);

        var offerId = await SendOfferAsync(hiredApp);
        var worker = _factory.CreateClientFor(OaoHelpers.Worker);
        Assert.Equal(HttpStatusCode.OK,
            (await worker.PostAsync($"/api/users/offers/{offerId}/accept", content: null)).StatusCode);

        await DrainOutboxAsync();

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The unserved applicant (Worker2) is told the opportunity filled.
        var unserved = await db.Notifications.Where(n =>
            n.RecipientType == NotificationRecipientType.User
            && n.RecipientId == OaoHelpers.Worker2.Sub
            && n.ResourceId == opp.ToString()
            && n.Type == NotificationTypes.Opportunity).ToListAsync();
        Assert.Single(unserved);

        // The hired applicant (Worker, holds the active offer) is excluded.
        var hired = await db.Notifications.Where(n =>
            n.RecipientType == NotificationRecipientType.User
            && n.RecipientId == OaoHelpers.Worker.Sub
            && n.ResourceId == opp.ToString()
            && n.Type == NotificationTypes.Opportunity).ToListAsync();
        Assert.Empty(hired);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EventTransition_NotifiesEventEstablishment(bool started)
    {
        Guid eventId;
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ev = new Event(Guid.NewGuid(), _establishment, Guid.NewGuid(), "My Event", "Desc", seasonId: null);
            db.Events.Add(ev);
            // Status sync would have staged this row alongside the transition.
            db.OutboxEvents.Add(new OutboxEvent(
                Guid.NewGuid(),
                started ? EventEventTypes.Started : EventEventTypes.Finished,
                nameof(Event), ev.Id, "{}", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            eventId = ev.Id;
        }

        await DrainOutboxAsync();

        using var s = _factory.CreateDbScope();
        var db2 = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifs = await db2.Notifications.Where(n =>
            n.RecipientType == NotificationRecipientType.Establishment
            && n.RecipientId == _establishment.ToString()
            && n.ResourceId == eventId.ToString()
            && n.Type == NotificationTypes.Event).ToListAsync();
        Assert.Single(notifs);
        Assert.Equal(started ? "Your event has started today" : "Your event has ended today", notifs[0].Message);
    }

    [Fact]
    public async Task OpportunityExpired_NotifiesApplicants()
    {
        var opp = await OaoHelpers.SeedOpportunityAsync(_factory, _establishment, name: "Expiring");
        await OaoHelpers.SeedApplicationAsync(_factory, opp, applicantUserId: OaoHelpers.Worker.Sub);

        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.OutboxEvents.Add(new OutboxEvent(
                Guid.NewGuid(), OpportunityEventTypes.Expired, nameof(Opportunity), opp, "{}", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        await DrainOutboxAsync();

        using var s = _factory.CreateDbScope();
        var db2 = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifs = await db2.Notifications.Where(n =>
            n.RecipientType == NotificationRecipientType.User
            && n.RecipientId == OaoHelpers.Worker.Sub
            && n.ResourceId == opp.ToString()
            && n.Type == NotificationTypes.Opportunity).ToListAsync();
        Assert.Single(notifs);
    }
}
