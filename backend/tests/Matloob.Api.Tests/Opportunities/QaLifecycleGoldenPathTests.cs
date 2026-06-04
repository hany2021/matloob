using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Common;
using Matloob.Domain.Offers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// API-driven QA golden path for the hiring lifecycle, mapped to the business
/// QA reference (matloob-business-qa-v2.md Â§6). Each test is labelled with its
/// TC id and asserts the doc's expected result â€” except where the implementation
/// is known to diverge, which is asserted-as-actual and called out in a comment
/// (those become the QA findings). Phase order follows the dependency chain:
/// opportunity â†’ apply â†’ offer â†’ accept â†’ cancel â†’ evaluate.
/// </summary>
public sealed class QaLifecycleGoldenPathTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;
    private Guid _establishmentId;
    private Guid _vacancyCategoryId;

    public QaLifecycleGoldenPathTests(OpportunitiesApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);
        _establishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-QA-GOLDEN");

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _vacancyCategoryId = await db.OpportunityCategories
            .Where(c => c.ForVacancy && !c.IsOther && c.ParentId != null)
            .Select(c => c.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ===== Phase 2 â€” Events & Opportunities ==================================

    /// <summary>TC-E03 / BR-02: an opportunity must be tied to an event â€” a
    /// create with no event reference is rejected.</summary>
    [Fact]
    public async Task TC_E03_CreateOpportunityWithoutEvent_IsBlocked()
    {
        var owner = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var resp = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities",
            new
            {
                // event_id intentionally omitted
                opportunity_category_id = _vacancyCategoryId,
                name = "No event opp",
                description = "Description long enough for validation.",
                start_date = today.AddDays(5).ToString("yyyy-MM-dd"),
                end_date = today.AddDays(15).ToString("yyyy-MM-dd"),
                location_title = "Riyadh",
                lat = 24.7m,
                lon = 46.6m,
                required_personnel = 3,
            });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    /// <summary>TC-E04: an individual applies to an opportunity from Explore; the
    /// application is recorded and visible to the opportunity owner.</summary>
    [Fact]
    public async Task TC_E04_IndividualAppliesToOpportunity_IsRecorded()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId, name: "Apply target");
        var worker = _factory.CreateClientFor(OaoHelpers.Worker);
        var owner = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);

        var apply = await worker.PostAsync($"/api/users/opportunities/{oppId}/apply", content: null);
        Assert.Equal(HttpStatusCode.Created, apply.StatusCode);

        var applicants = await owner.GetStringAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities/{oppId}/applications");
        Assert.True(JsonDocument.Parse(applicants).RootElement.DataOf().GetArrayLength() >= 1);
    }

    /// <summary>Permission (Â§4.4): an individual cannot send a job offer.</summary>
    [Fact]
    public async Task TC_PERM_IndividualCannotSendOffer()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId, name: "Perm opp");
        var appId = await OaoHelpers.SeedApplicationAsync(_factory, oppId, applicantUserId: OaoHelpers.Worker.Sub);

        // Worker (an individual, not a member of the establishment) tries to send.
        var worker = _factory.CreateClientFor(OaoHelpers.Worker);
        var resp = await worker.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/offers/send",
            new { applicant_id = appId, monthly_salary = 5000m });
        Assert.True(resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected 404/403, got {(int)resp.StatusCode}.");
    }

    // ===== Phase 3 â€” Offers & Contracts =====================================

    /// <summary>TC-O01 + TC-O02 + TC-O03: organizer sends an offer to an applicant
    /// â†’ arrives Pending; single contract type (no type field required); no
    /// saudization/Ajeer/contract keys in the payload.</summary>
    [Fact]
    public async Task TC_O01_O02_O03_OrganizerSendsOffer_Pending_Clean()
    {
        var (offerId, sendJson) = await SeedAppAndSendOfferAsync("Send opp");
        Assert.False(string.IsNullOrEmpty(sendJson));

        using var doc = JsonDocument.Parse(sendJson);
        var data = doc.RootElement.DataOf();
        Assert.Equal("pending", data.GetProperty("status").GetString());      // TC-O01
        Assert.NotEqual(Guid.Empty, offerId);

        // TC-O02 (single contract type): no contract-type selector concept.
        // TC-O03 (no saudization/Ajeer): no such keys anywhere in the payload.
        foreach (var key in EnumerateKeys(doc.RootElement))
        {
            var k = key.ToLowerInvariant();
            Assert.DoesNotContain("ajeer", k);
            Assert.DoesNotContain("saudization", k);
            Assert.DoesNotContain("contract_type", k);
        }
    }

    /// <summary>TC-O04 / BR-15: the individual accepts the offer â†’ it becomes the
    /// active contract (status Accepted, accepted_at stamped).</summary>
    [Fact]
    public async Task TC_O04_IndividualAcceptsOffer_BecomesActiveContract()
    {
        var (offerId, _) = await SeedAppAndSendOfferAsync("Accept opp");
        var worker = _factory.CreateClientFor(OaoHelpers.Worker);

        var accept = await worker.PostAsync($"/api/users/offers/{offerId}/accept", content: null);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        using var doc = JsonDocument.Parse(await accept.Content.ReadAsStringAsync());
        var data = doc.RootElement.DataOf();
        Assert.Equal("accepted", data.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, data.GetProperty("accepted_at").ValueKind);
    }

    /// <summary>TC-O06 / BR-06: an individual cannot cancel directly â€” they open a
    /// cancellation request with a reason (status â†’ CancellationRequested).</summary>
    [Fact]
    public async Task TC_O06_IndividualRequestsCancellation_NotDirectCancel()
    {
        var (offerId, _) = await SeedAppAndSendOfferAsync("User cancel opp");
        var worker = _factory.CreateClientFor(OaoHelpers.Worker);
        await worker.PostAsync($"/api/users/offers/{offerId}/accept", content: null);

        Guid reasonId;
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            reasonId = await db.OfferCancellationReasons.Select(r => r.Id).FirstAsync();
        }

        var cancel = await worker.PostAsJsonAsync("/api/users/offers/cancel",
            new { offer_id = offerId, reason_id = reasonId });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var offer = await OaoHelpers.LoadOfferAsync(_factory, offerId);
        Assert.Equal(OfferStatus.CancellationRequested, offer!.Status);  // a request, not a direct Canceled
    }

    /// <summary>TC-O05 / BR-07: the organizer cancels an individual's contract with
    /// a reason. (Documents actual behaviour: the establishment "cancel" opens the
    /// two-step cancellation flow â†’ CancellationRequested, awaiting the other
    /// party â€” same mechanism as the user side.)</summary>
    [Fact]
    public async Task TC_O05_OrganizerCancelsContractWithReason()
    {
        var (offerId, _) = await SeedAppAndSendOfferAsync("Org cancel opp");
        var owner = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var worker = _factory.CreateClientFor(OaoHelpers.Worker);
        await worker.PostAsync($"/api/users/offers/{offerId}/accept", content: null);

        Guid reasonId;
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            reasonId = await db.OfferCancellationReasons.Select(r => r.Id).FirstAsync();
        }

        var cancel = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/offers/cancel",
            new { offer_id = offerId, reason_id = reasonId });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var offer = await OaoHelpers.LoadOfferAsync(_factory, offerId);
        Assert.True(offer!.Status is OfferStatus.CancellationRequested or OfferStatus.Canceled,
            $"Unexpected status after organizer cancel: {offer.Status}.");
    }

    // ===== Phase 4 â€” Evaluation =============================================

    /// <summary>TC-V01: after acceptance both parties evaluate â†’ offer Completed.</summary>
    [Fact]
    public async Task TC_V01_BothSidesEvaluate_OfferCompleted()
    {
        var (offerId, _) = await SeedAppAndSendOfferAsync("Eval opp");
        var owner = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var worker = _factory.CreateClientFor(OaoHelpers.Worker);
        await worker.PostAsync($"/api/users/offers/{offerId}/accept", content: null);

        var w = await worker.PostAsJsonAsync("/api/v1/users/evaluations",
            new { offer_id = offerId, rating = 5, recommend_for_future_opportunities = true });
        Assert.Equal(HttpStatusCode.Created, w.StatusCode);

        var e = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/evaluations",
            new { offer_id = offerId, rating = 5, recommend_for_future_opportunities = true });
        Assert.Equal(HttpStatusCode.Created, e.StatusCode);

        var offer = await OaoHelpers.LoadOfferAsync(_factory, offerId);
        Assert.Equal(OfferStatus.Completed, offer!.Status);
    }

    /// <summary>
    /// TC-V02 â€” QA FINDING / DIVERGENCE. The doc (BR-13, inferred) says evaluation
    /// happens only AFTER the contract ends. The implementation allows evaluating
    /// an offer that is merely <c>Accepted</c> (job not started/ended). This test
    /// asserts the ACTUAL behaviour (allowed) so it is recorded; the divergence is
    /// reported to product, not silently passed.
    /// </summary>
    [Fact]
    public async Task TC_V02_EvaluateBeforeContractEnd_CurrentlyAllowed_DIVERGENCE()
    {
        var (offerId, _) = await SeedAppAndSendOfferAsync("Early eval opp");
        var worker = _factory.CreateClientFor(OaoHelpers.Worker);
        await worker.PostAsync($"/api/users/offers/{offerId}/accept", content: null);

        // Offer is Accepted, not finished. Per the doc this should be blocked.
        var early = await worker.PostAsJsonAsync("/api/v1/users/evaluations",
            new { offer_id = offerId, rating = 4, recommend_for_future_opportunities = true });

        // ACTUAL: the API permits it (Accepted is an evaluable state).
        Assert.Equal(HttpStatusCode.Created, early.StatusCode);
    }

    // -- helpers --------------------------------------------------------------

    private async Task<(Guid OfferId, string SendJson)> SeedAppAndSendOfferAsync(string oppName)
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId, name: oppName);
        var appId = await OaoHelpers.SeedApplicationAsync(_factory, oppId, applicantUserId: OaoHelpers.Worker.Sub);

        var owner = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var send = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/offers/send",
            new { applicant_id = appId, monthly_salary = 5000m });
        send.EnsureSuccessStatusCode();
        var json = await send.Content.ReadAsStringAsync();
        var offerId = JsonDocument.Parse(json).RootElement.DataOf().GetProperty("id").GetGuid();
        return (offerId, json);
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
                    foreach (var n in EnumerateKeys(item)) yield return n;
                break;
        }
    }
}
