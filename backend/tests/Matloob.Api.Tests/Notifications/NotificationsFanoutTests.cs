using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Events.Dispatcher;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Common;
using Matloob.Api.Tests.Opportunities;
using Matloob.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Notifications;

/// <summary>
/// End-to-end fanout test: accepting an offer emits <c>offer.accepted</c>, and
/// a dispatcher pass turns it into a database notification addressed to the
/// sender establishment.
/// </summary>
public sealed class NotificationsFanoutTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;
    private Guid _establishment;
    private Guid _applicationId;

    public NotificationsFanoutTests(OpportunitiesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);
        _establishment = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-NOTIF-FANOUT");
        var opportunity = await OaoHelpers.SeedOpportunityAsync(
            _factory, _establishment, name: "Vacancy", forVacancy: true);
        _applicationId = await OaoHelpers.SeedApplicationAsync(
            _factory, opportunity, applicantUserId: OaoHelpers.Worker.Sub);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task OfferAccepted_CreatesEstablishmentNotification()
    {
        var owner = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var send = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishment}/offers/send",
            new { applicant_id = _applicationId, monthly_salary = 6000m });
        send.EnsureSuccessStatusCode();
        Guid offerId;
        using (var sdoc = JsonDocument.Parse(await send.Content.ReadAsStringAsync()))
            offerId = sdoc.RootElement.DataOf().GetProperty("id").GetGuid();

        var worker = _factory.CreateClientFor(OaoHelpers.Worker);
        var accept = await worker.PostAsync($"/api/users/offers/{offerId}/accept", content: null);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        // Drain the outbox -> fanout creates the notification.
        using (var scope = _factory.CreateDbScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcherService>();
            await dispatcher.DispatchAsync(CancellationToken.None);
        }

        // Assert the notification landed for the sender establishment.
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifs = await db.Notifications
                .Where(n => n.RecipientType == NotificationRecipientType.Establishment
                         && n.RecipientId == _establishment.ToString())
                .ToListAsync();
            Assert.Single(notifs);
            Assert.Equal(NotificationTypes.SentOffer, notifs[0].Type);
            Assert.Equal(offerId.ToString(), notifs[0].ResourceId);
        }

        // And the establishment's unread-count endpoint reflects it.
        using var countDoc = JsonDocument.Parse(
            await (await owner.GetAsync(
                $"/api/establishments/notifications/unread-count?establishment_id={_establishment}"))
                .Content.ReadAsStringAsync());
        Assert.Equal(1, countDoc.RootElement.GetProperty("count").GetInt32());
    }
}
