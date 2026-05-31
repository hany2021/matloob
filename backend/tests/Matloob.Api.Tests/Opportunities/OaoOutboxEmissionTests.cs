using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Common;
using Matloob.Domain.Applications;
using Matloob.Domain.Evaluations;
using Matloob.Domain.Offers;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Matloob.Domain.Establishments;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// Verifies that every write endpoint touched by Phase OAO-3/4/5/6
/// emits the correct outbox event types. Each test drives one endpoint
/// and asserts the corresponding row landed in <c>outbox_events</c>
/// with matching <c>event_type</c> + <c>aggregate_id</c>.
/// </summary>
public sealed class OaoOutboxEmissionTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;

    private Guid _establishmentId;
    private Guid _vacancyCategoryId;

    private static readonly TestUser SponsorOwner = new(
        Sub: "oao-outbox-sponsor", Roles: new[] { "matloob_user" });
    private Guid _sponsorEstablishmentId;

    public OaoOutboxEmissionTests(OpportunitiesApiFactory factory) { _factory = factory; }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, SponsorOwner.Sub);

        _establishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-OAO-OUTBOX");
        _sponsorEstablishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, SponsorOwner.Sub, "CR-OAO-OUTBOX-SP");

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _vacancyCategoryId = await db.OpportunityCategories
            .Where(c => c.ForVacancy && !c.IsOther && c.ParentId != null)
            .Select(c => c.Id).FirstAsync();
        var sponsor = await db.Establishments.FirstAsync(e => e.Id == _sponsorEstablishmentId);
        typeof(Establishment).GetProperty(nameof(Establishment.IsSponsor))!.SetValue(sponsor, true);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task OpportunityWrites_EmitExpectedEvents()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);

        // Create
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var createResp = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities",
            new
            {
                event_id = Guid.NewGuid(),
                opportunity_category_id = _vacancyCategoryId,
                name = "Outbox opp",
                description = "Description text long enough.",
                start_date = today.AddDays(5).ToString("yyyy-MM-dd"),
                end_date = today.AddDays(15).ToString("yyyy-MM-dd"),
                location_title = "Riyadh",
                lat = 24.7m,
                lon = 46.6m,
                required_personnel = 5,
            });
        createResp.EnsureSuccessStatusCode();
        using var createDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var oppGuid = createDoc.RootElement.DataOf().GetProperty("id").GetGuid();

        Assert.Equal(1, await OaoHelpers.CountOutboxEventsAsync(
            _factory, OpportunityEventTypes.Created, oppGuid));

        // Update
        await client.PatchAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities/{oppGuid}",
            new { name = "Outbox opp v2" });
        Assert.Equal(1, await OaoHelpers.CountOutboxEventsAsync(
            _factory, OpportunityEventTypes.Updated, oppGuid));

        // End
        await client.PatchAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities/{oppGuid}/end",
            content: null);
        Assert.Equal(1, await OaoHelpers.CountOutboxEventsAsync(
            _factory, OpportunityEventTypes.Ended, oppGuid));

        // Delete
        await client.DeleteAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities/{oppGuid}");
        Assert.Equal(1, await OaoHelpers.CountOutboxEventsAsync(
            _factory, OpportunityEventTypes.Deleted, oppGuid));
    }

    [Fact]
    public async Task UserApply_EmitsApplicationSubmitted()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _establishmentId, name: "Apply outbox", forVacancy: true);
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        await client.PostAsync($"/api/v1/users/opportunities/{oppId}/apply", content: null);

        Assert.True(await OaoHelpers.CountOutboxEventsAsync(
            _factory, ApplicationEventTypes.Submitted) >= 1);
    }

    [Fact]
    public async Task SendOffer_WithSponsor_EmitsCreatedAndSponsorPending()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _establishmentId, name: "Offer outbox", forVacancy: true);
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, oppId, applicantUserId: OaoHelpers.Worker.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/offers/send",
            new
            {
                applicant_id = appId,
                monthly_salary = 7000m,
                sponsor_id = _sponsorEstablishmentId,
            });
        response.EnsureSuccessStatusCode();

        Assert.True(await OaoHelpers.CountOutboxEventsAsync(
            _factory, OfferEventTypes.Created) >= 1);
        Assert.True(await OaoHelpers.CountOutboxEventsAsync(
            _factory, OfferEventTypes.SponsorApprovalPending) >= 1);
    }

    [Fact]
    public async Task FullOfferAndEvaluationJourney_EmitsAllEvents()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _establishmentId, name: "Journey opp", forVacancy: true);
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, oppId, applicantUserId: OaoHelpers.Worker.Sub);

        var owner = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var worker = _factory.CreateClientFor(OaoHelpers.Worker);

        // send -> accept
        var sendResp = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/offers/send",
            new { applicant_id = appId, monthly_salary = 5000m });
        sendResp.EnsureSuccessStatusCode();
        using var sendDoc = JsonDocument.Parse(await sendResp.Content.ReadAsStringAsync());
        var offerGuid = sendDoc.RootElement.DataOf().GetProperty("id").GetGuid();

        await worker.PostAsync($"/api/v1/users/offers/{offerGuid}/accept", content: null);
        Assert.Equal(1, await OaoHelpers.CountOutboxEventsAsync(
            _factory, OfferEventTypes.Accepted, offerGuid));

        // both sides evaluate
        await worker.PostAsJsonAsync("/api/v1/users/evaluations",
            new { offer_id = offerGuid, rating = 5, recommend_for_future_opportunities = true });
        await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/evaluations",
            new { offer_id = offerGuid, rating = 5, recommend_for_future_opportunities = true });

        Assert.True(await OaoHelpers.CountOutboxEventsAsync(
            _factory, EvaluationEventTypes.Submitted) >= 2);
    }
}
