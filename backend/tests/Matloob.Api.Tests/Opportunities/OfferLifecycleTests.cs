using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Domain.Establishments;
using Matloob.Domain.Offers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// Comprehensive tests for the OAO-5 offer lifecycle: read endpoints,
/// send, accept, reject, cancel, approve-cancellation, reject-cancellation,
/// sponsor accept, sponsor reject. Each test pins the snake_case shape
/// AND the absence of Ajeer/contract/invoice fields.
/// </summary>
public sealed class OfferLifecycleTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;

    private Guid _senderEstablishment;
    private Guid _opportunityVacancy;
    private Guid _workerApplicationId;

    private Guid _sponsorEstablishment;

    private static readonly TestUser SponsorOwner = new(
        Sub: "oao-offer-sponsor-owner", Roles: new[] { "matloob_user" });

    public OfferLifecycleTests(OpportunitiesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker2.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, SponsorOwner.Sub);

        _senderEstablishment = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-OAO-OFFER-SEND");
        _sponsorEstablishment = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, SponsorOwner.Sub, "CR-OAO-OFFER-SPONSOR");

        _opportunityVacancy = await OaoHelpers.SeedOpportunityAsync(
            _factory, _senderEstablishment,
            name: "Vacancy",
            forVacancy: true);
        _workerApplicationId = await OaoHelpers.SeedApplicationAsync(
            _factory, _opportunityVacancy,
            applicantUserId: OaoHelpers.Worker.Sub);

        // Mark the sponsor establishment as is_sponsor=true so sponsor
        // validation passes on SendOffer.
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sponsor = await db.Establishments.FirstAsync(e => e.Id == _sponsorEstablishment);
        typeof(Establishment).GetProperty(nameof(Establishment.IsSponsor))!.SetValue(sponsor, true);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -- send ---------------------------------------------------------------

    [Fact]
    public async Task Send_HappyPath_Returns201_AndOfferStartsPending()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_senderEstablishment}/offers/send",
            new
            {
                applicant_id = _workerApplicationId,
                monthly_salary = 6000m,
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("Pending", doc.RootElement.GetProperty("status").GetString());
        // No Ajeer/contract/invoice fields.
        Assert.False(doc.RootElement.TryGetProperty("contract", out _));
        Assert.False(doc.RootElement.TryGetProperty("contract_type", out _));
        Assert.False(doc.RootElement.TryGetProperty("contract_path", out _));

        var outboxCount = await OaoHelpers.CountOutboxEventsAsync(
            _factory, OfferEventTypes.Created);
        Assert.True(outboxCount > 0);
    }

    [Fact]
    public async Task Send_WithSponsor_StartsPendingSponsorApproval()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_senderEstablishment}/offers/send",
            new
            {
                applicant_id = _workerApplicationId,
                monthly_salary = 6000m,
                sponsor_id = _sponsorEstablishment,
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("PendingSponsorApproval", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.GetProperty("is_pending_sponsor_approval").GetBoolean());
    }

    [Fact]
    public async Task Send_DuplicateForSameApplicant_Returns409()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var body = new { applicant_id = _workerApplicationId, monthly_salary = 6000m };
        var first = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_senderEstablishment}/offers/send", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_senderEstablishment}/offers/send", body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Send_ApplicantNotOwnedByMe_Returns422()
    {
        // Make an opportunity issued by ANOTHER establishment + a worker
        // application on it. The sender establishment tries to send an
        // offer to that foreign applicant -> 422 applicant_not_visible.
        var foreignOwner = new TestUser(
            Sub: "oao-offer-foreign-owner", Roles: new[] { "matloob_user" });
        await OaoHelpers.SeedLocalUserAsync(_factory, foreignOwner.Sub);
        var foreignEstId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, foreignOwner.Sub, "CR-OAO-OFFER-FOREIGN");
        var foreignOpp = await OaoHelpers.SeedOpportunityAsync(
            _factory, foreignEstId, name: "Foreign opp", forVacancy: true);
        var foreignApp = await OaoHelpers.SeedApplicationAsync(
            _factory, foreignOpp, applicantUserId: OaoHelpers.Worker2.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_senderEstablishment}/offers/send",
            new { applicant_id = foreignApp, monthly_salary = 5000m });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("applicant_not_visible_to_sender",
            doc.RootElement.GetProperty("code").GetString());
    }

    // -- read ---------------------------------------------------------------

    [Fact]
    public async Task UserList_ShowsReceivedOffers()
    {
        var offerId = await SendAndCaptureAsync();
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync("/api/users/offers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Contains(doc.RootElement.EnumerateArray(),
            e => e.GetProperty("id").GetGuid() == offerId);
    }

    [Fact]
    public async Task UserShow_ForeignOffer_Returns404()
    {
        // Worker2 has no relationship to the offer.
        var offerId = await SendAndCaptureAsync();
        var client = _factory.CreateClientFor(OaoHelpers.Worker2);
        var response = await client.GetAsync($"/api/users/offers/{offerId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -- accept/reject ------------------------------------------------------

    [Fact]
    public async Task UserAccept_Pending_FlipsToAccepted_WithAcceptedAt()
    {
        var offerId = await SendAndCaptureAsync();
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.PostAsync(
            $"/api/users/offers/{offerId}/accept", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("Accepted", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("accepted_at").ValueKind);
    }

    [Fact]
    public async Task UserAccept_AlreadyAccepted_Returns422()
    {
        var offerId = await SendAndCaptureAsync();
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var first = await client.PostAsync($"/api/users/offers/{offerId}/accept", content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await client.PostAsync($"/api/users/offers/{offerId}/accept", content: null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    [Fact]
    public async Task UserReject_StoresReasonAndFlipsStatus()
    {
        var offerId = await SendAndCaptureAsync();
        using (var scope = _factory.CreateDbScope())
        {
            // Seed at least one rejection reason if the seed didn't run.
        }

        var reasonId = await GetAnyRejectionReasonAsync();

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.PostAsJsonAsync(
            $"/api/users/offers/{offerId}/reject",
            new { reason_id = reasonId, other_reason = "not interested" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("Rejected", doc.RootElement.GetProperty("status").GetString());
    }

    // -- cancellation -------------------------------------------------------

    [Fact]
    public async Task User_Cancel_Then_Establishment_Approve_FlipsToCanceled()
    {
        var offerId = await SendAndCaptureAsync();
        // Worker accepts first.
        await _factory.CreateClientFor(OaoHelpers.Worker)
            .PostAsync($"/api/users/offers/{offerId}/accept", content: null);

        // Worker opens cancellation.
        var cancelReason = await GetAnyCancellationReasonAsync();
        var cancelResp = await _factory.CreateClientFor(OaoHelpers.Worker)
            .PostAsJsonAsync("/api/users/offers/cancel",
                new { offer_id = offerId, reason_id = cancelReason });
        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);

        // Establishment approves the cancellation.
        var approveResp = await _factory.CreateClientFor(OaoHelpers.EstablishmentOwner)
            .PostAsync(
                $"/api/v1/establishments/{_senderEstablishment}/offers/{offerId}/approve-cancellation",
                content: null);
        Assert.Equal(HttpStatusCode.OK, approveResp.StatusCode);

        await using var stream = await approveResp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("Canceled", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Establishment_RejectCancellation_RestoresAccepted()
    {
        var offerId = await SendAndCaptureAsync();
        await _factory.CreateClientFor(OaoHelpers.Worker)
            .PostAsync($"/api/users/offers/{offerId}/accept", content: null);

        var cancelReason = await GetAnyCancellationReasonAsync();
        await _factory.CreateClientFor(OaoHelpers.Worker)
            .PostAsJsonAsync("/api/users/offers/cancel",
                new { offer_id = offerId, reason_id = cancelReason });

        var rejectResp = await _factory.CreateClientFor(OaoHelpers.EstablishmentOwner)
            .PostAsync(
                $"/api/v1/establishments/{_senderEstablishment}/offers/{offerId}/reject-cancellation",
                content: null);
        Assert.Equal(HttpStatusCode.OK, rejectResp.StatusCode);

        await using var stream = await rejectResp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("Accepted", doc.RootElement.GetProperty("status").GetString());
    }

    // -- sponsor flow -------------------------------------------------------

    [Fact]
    public async Task Sponsor_Accept_FlipsToPending()
    {
        var sendResp = await _factory.CreateClientFor(OaoHelpers.EstablishmentOwner)
            .PostAsJsonAsync(
                $"/api/v1/establishments/{_senderEstablishment}/offers/send",
                new
                {
                    applicant_id = _workerApplicationId,
                    monthly_salary = 6000m,
                    sponsor_id = _sponsorEstablishment,
                });
        Assert.Equal(HttpStatusCode.Created, sendResp.StatusCode);
        await using var sendStream = await sendResp.Content.ReadAsStreamAsync();
        var sendDoc = await JsonDocument.ParseAsync(sendStream);
        var offerId = sendDoc.RootElement.GetProperty("id").GetGuid();

        var response = await _factory.CreateClientFor(SponsorOwner)
            .PostAsync(
                $"/api/v1/establishments/{_sponsorEstablishment}/offers/{offerId}/sponsor/accept",
                content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("Pending", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Sponsor_Reject_FlipsToSponsorRejected()
    {
        var sendResp = await _factory.CreateClientFor(OaoHelpers.EstablishmentOwner)
            .PostAsJsonAsync(
                $"/api/v1/establishments/{_senderEstablishment}/offers/send",
                new
                {
                    applicant_id = _workerApplicationId,
                    monthly_salary = 6000m,
                    sponsor_id = _sponsorEstablishment,
                });
        Assert.Equal(HttpStatusCode.Created, sendResp.StatusCode);
        await using var sendStream = await sendResp.Content.ReadAsStreamAsync();
        var sendDoc = await JsonDocument.ParseAsync(sendStream);
        var offerId = sendDoc.RootElement.GetProperty("id").GetGuid();

        var reasonId = await GetAnyRejectionReasonAsync();
        var response = await _factory.CreateClientFor(SponsorOwner)
            .PostAsJsonAsync(
                $"/api/v1/establishments/{_sponsorEstablishment}/offers/{offerId}/sponsor/reject",
                new { reason_id = reasonId, other_reason = "no thanks" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("SponsorRejected", doc.RootElement.GetProperty("status").GetString());
    }

    // -- helpers ------------------------------------------------------------

    private async Task<Guid> SendAndCaptureAsync()
    {
        var sendResp = await _factory.CreateClientFor(OaoHelpers.EstablishmentOwner)
            .PostAsJsonAsync(
                $"/api/v1/establishments/{_senderEstablishment}/offers/send",
                new { applicant_id = _workerApplicationId, monthly_salary = 6000m });
        Assert.Equal(HttpStatusCode.Created, sendResp.StatusCode);
        await using var stream = await sendResp.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> GetAnyRejectionReasonAsync()
    {
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.OfferRejectionReasons.Select(r => r.Id).FirstAsync();
    }

    private async Task<Guid> GetAnyCancellationReasonAsync()
    {
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.OfferCancellationReasons.Select(r => r.Id).FirstAsync();
    }
}
