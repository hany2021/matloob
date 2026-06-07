using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Common;
using Matloob.Domain.Establishments;
using Matloob.Domain.Offers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// End-to-end journey tests for the full OAO migration. Each test
/// drives the entire happy path of one journey via HTTP â€” opportunity
/// publish â†’ apply â†’ offer â†’ accept â†’ cancel â†’ evaluate â€” and asserts
/// no Ajeer / contract / invoice / notice_path / contract_path /
/// show_print_notice key appears in any of the responses along the
/// way.
/// </summary>
public sealed class OaoEndToEndJourneyTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private static readonly string[] ForbiddenSubstrings =
    [
        "ajeer", "contract", "invoice", "notice_path", "show_print_notice",
    ];

    private readonly OpportunitiesApiFactory _factory;
    private Guid _establishmentId;
    private Guid _sponsorEstablishmentId;
    private Guid _vacancyCategoryId;

    private static readonly TestUser SponsorOwner = new(
        Sub: "oao-e2e-sponsor-owner", Roles: new[] { "matloob_user" });

    public OaoEndToEndJourneyTests(OpportunitiesApiFactory factory) { _factory = factory; }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker2.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, SponsorOwner.Sub);

        _establishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-OAO-E2E-A");
        _sponsorEstablishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, SponsorOwner.Sub, "CR-OAO-E2E-SP");

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _vacancyCategoryId = await db.OpportunityCategories
            .Where(c => c.ForVacancy && !c.IsOther && c.ParentId != null)
            .Select(c => c.Id).FirstAsync();
        var sponsor = await db.Establishments.FirstAsync(e => e.Id == _sponsorEstablishmentId);
        typeof(Establishment).GetProperty(nameof(Establishment.IsSponsor))!
            .SetValue(sponsor, true);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -- 1. Opportunity journey: publish â†’ browse â†’ apply â†’ owner-list -----

    [Fact]
    public async Task OpportunityJourney_PublishBrowseApplyOwnerList_Clean()
    {
        var owner = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var worker = _factory.CreateClientFor(OaoHelpers.Worker);

        // Owner creates an opportunity.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var createResp = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities",
            new
            {
                event_id = Guid.NewGuid(),
                opportunity_category_id = _vacancyCategoryId,
                name = "E2E opp",
                description = "End to end opportunity description.",
                start_date = today.AddDays(5).ToString("yyyy-MM-dd"),
                end_date = today.AddDays(15).ToString("yyyy-MM-dd"),
                location_title = "Riyadh",
                lat = 24.7m,
                lon = 46.6m,
                required_personnel = 5,
            });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var createJson = await createResp.Content.ReadAsStringAsync();
        AssertNoForbiddenKeys(createJson, "POST opportunity");
        var oppId = JsonDocument.Parse(createJson).RootElement.DataOf().GetProperty("id").GetGuid();

        // Worker browses (should see the new opportunity).
        var browseJson = await worker.GetStringAsync("/api/users/opportunities");
        AssertNoForbiddenKeys(browseJson, "browse opportunities");
        Assert.Contains(JsonDocument.Parse(browseJson).RootElement.DataOf().EnumerateArray(),
            e => e.GetProperty("id").GetGuid() == oppId);

        // Worker applies.
        var applyResp = await worker.PostAsync(
            $"/api/users/opportunities/{oppId}/apply", content: null);
        Assert.Equal(HttpStatusCode.Created, applyResp.StatusCode);

        // Owner lists own-opportunity applicants.
        var applicantsJson = await owner.GetStringAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities/{oppId}/applications");
        AssertNoForbiddenKeys(applicantsJson, "own-opportunity applications");
        Assert.True(JsonDocument.Parse(applicantsJson).RootElement.DataOf().GetArrayLength() >= 1);
    }

    // -- 2. Offer journey: send â†’ accept ----------------------------------

    [Fact]
    public async Task OfferJourney_SendAndAccept_Clean()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _establishmentId, name: "Offer journey", forVacancy: true);
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, oppId, applicantUserId: OaoHelpers.Worker.Sub);

        var owner = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var sendResp = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/offers/send",
            new { applicant_id = appId, monthly_salary = 5000m });
        Assert.Equal(HttpStatusCode.Created, sendResp.StatusCode);
        var sendJson = await sendResp.Content.ReadAsStringAsync();
        AssertNoForbiddenKeys(sendJson, "send offer");
        var offerId = JsonDocument.Parse(sendJson).RootElement.DataOf().GetProperty("id").GetGuid();

        var worker = _factory.CreateClientFor(OaoHelpers.Worker);
        var listJson = await worker.GetStringAsync("/api/users/offers");
        AssertNoForbiddenKeys(listJson, "user offers list");

        var acceptResp = await worker.PostAsync(
            $"/api/users/offers/{offerId}/accept", content: null);
        Assert.Equal(HttpStatusCode.OK, acceptResp.StatusCode);
        var acceptJson = await acceptResp.Content.ReadAsStringAsync();
        AssertNoForbiddenKeys(acceptJson, "accept offer");

        using var acceptDoc = JsonDocument.Parse(acceptJson);
        var acceptData = acceptDoc.RootElement.DataOf();
        Assert.Equal("accepted", acceptData.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, acceptData.GetProperty("accepted_at").ValueKind);
    }

    // -- 3. Cancellation journey: accept â†’ cancel â†’ approve --------------

    [Fact]
    public async Task CancellationJourney_AcceptCancelApprove_Clean()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _establishmentId, name: "Cancel journey");
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, oppId, applicantUserId: OaoHelpers.Worker.Sub);
        var owner = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var worker = _factory.CreateClientFor(OaoHelpers.Worker);

        var sendResp = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/offers/send",
            new { applicant_id = appId, monthly_salary = 5000m });
        var offerId = JsonDocument.Parse(await sendResp.Content.ReadAsStringAsync())
            .RootElement.DataOf().GetProperty("id").GetGuid();

        await worker.PostAsync($"/api/users/offers/{offerId}/accept", content: null);

        // Worker opens cancellation.
        using (var scope = _factory.CreateDbScope())
        {
            // Use any cancellation reason from the seed.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reasonId = await db.OfferCancellationReasons.Select(r => r.Id).FirstAsync();
            var cancelResp = await worker.PostAsJsonAsync("/api/users/offers/cancel",
                new { offer_id = offerId, reason_id = reasonId });
            Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);
        }

        var approveResp = await owner.PostAsync(
            $"/api/v1/establishments/{_establishmentId}/offers/{offerId}/approve-cancellation",
            content: null);
        Assert.Equal(HttpStatusCode.OK, approveResp.StatusCode);
        var approveJson = await approveResp.Content.ReadAsStringAsync();
        AssertNoForbiddenKeys(approveJson, "approve cancellation");

        using var approveDoc = JsonDocument.Parse(approveJson);
        Assert.Equal("canceled", approveDoc.RootElement.DataOf().GetProperty("status").GetString());
    }

    // -- 4. Sponsor journey: send-with-sponsor â†’ sponsor-accept ----------

    [Fact]
    public async Task SponsorJourney_SendWithSponsor_SponsorAccept_Clean()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _establishmentId, name: "Sponsor journey");
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, oppId, applicantUserId: OaoHelpers.Worker.Sub);

        var owner = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var sendResp = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/offers/send",
            new
            {
                applicant_id = appId,
                monthly_salary = 5000m,
                sponsor_id = _sponsorEstablishmentId,
            });
        Assert.Equal(HttpStatusCode.Created, sendResp.StatusCode);
        var sendJson = await sendResp.Content.ReadAsStringAsync();
        AssertNoForbiddenKeys(sendJson, "send offer with sponsor");
        var offerId = JsonDocument.Parse(sendJson).RootElement.DataOf().GetProperty("id").GetGuid();
        Assert.Equal("pending_sponsor_approval",
            JsonDocument.Parse(sendJson).RootElement.DataOf().GetProperty("status").GetString());

        var sponsor = _factory.CreateClientFor(SponsorOwner);

        // Sponsor sees the pending approval.
        var pendingJson = await sponsor.GetStringAsync(
            $"/api/v1/establishments/{_sponsorEstablishmentId}/offers/{offerId}/pending-sponsor-approval");
        AssertNoForbiddenKeys(pendingJson, "sponsor pending");

        // Sponsor accepts.
        var sponsorAcceptResp = await sponsor.PostAsync(
            $"/api/v1/establishments/{_sponsorEstablishmentId}/offers/{offerId}/sponsor/accept",
            content: null);
        Assert.Equal(HttpStatusCode.OK, sponsorAcceptResp.StatusCode);
        var acceptJson = await sponsorAcceptResp.Content.ReadAsStringAsync();
        AssertNoForbiddenKeys(acceptJson, "sponsor accept");
        Assert.Equal("pending",
            JsonDocument.Parse(acceptJson).RootElement.DataOf().GetProperty("status").GetString());
    }

    // -- 5. Evaluation journey: accept â†’ both-sides-evaluate â†’ Completed --

    [Fact]
    public async Task EvaluationJourney_BothSidesEvaluate_OfferCompleted_Clean()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _establishmentId, name: "Eval journey");
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, oppId, applicantUserId: OaoHelpers.Worker.Sub);

        var owner = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var worker = _factory.CreateClientFor(OaoHelpers.Worker);

        var sendResp = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/offers/send",
            new { applicant_id = appId, monthly_salary = 5000m });
        var offerId = JsonDocument.Parse(await sendResp.Content.ReadAsStringAsync())
            .RootElement.DataOf().GetProperty("id").GetGuid();

        await worker.PostAsync($"/api/users/offers/{offerId}/accept", content: null);

        // Worker evaluates.
        var w = await worker.PostAsJsonAsync("/api/v1/users/evaluations",
            new { offer_id = offerId, rating = 5, recommend_for_future_opportunities = true });
        Assert.Equal(HttpStatusCode.Created, w.StatusCode);
        AssertNoForbiddenKeys(await w.Content.ReadAsStringAsync(), "user evaluation");

        // Establishment evaluates.
        var e = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/evaluations",
            new { offer_id = offerId, rating = 5, recommend_for_future_opportunities = true });
        Assert.Equal(HttpStatusCode.Created, e.StatusCode);
        AssertNoForbiddenKeys(await e.Content.ReadAsStringAsync(), "establishment evaluation");

        // Final offer state.
        var offer = await OaoHelpers.LoadOfferAsync(_factory, offerId);
        Assert.Equal(OfferStatus.Completed, offer!.Status);

        // Sweep current sent-offers list to confirm shape stays clean.
        var sentJson = await owner.GetStringAsync(
            $"/api/v1/establishments/{_establishmentId}/sent-offers");
        AssertNoForbiddenKeys(sentJson, "sent-offers after eval");
    }

    private static void AssertNoForbiddenKeys(string json, string where)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var key in EnumerateKeys(doc.RootElement))
        {
            // contracts_count (filled positions) substring-matches "contract"
            // but is a legitimate frontend field, not the dropped Ajeer concept.
            if (key.Equals("contracts_count", StringComparison.OrdinalIgnoreCase)) continue;
            var lower = key.ToLowerInvariant();
            foreach (var bad in ForbiddenSubstrings)
            {
                Assert.False(lower.Contains(bad),
                    $"Endpoint payload at {where} returned forbidden key '{key}' (matches '{bad}').");
            }
        }
    }

    private static IEnumerable<string> EnumerateKeys(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    yield return prop.Name;
                    foreach (var n in EnumerateKeys(prop.Value)) yield return n;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var n in EnumerateKeys(item)) yield return n;
                }
                break;
        }
    }
}
