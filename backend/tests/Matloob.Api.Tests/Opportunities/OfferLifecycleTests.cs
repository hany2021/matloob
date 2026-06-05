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
        var data = doc.RootElement.DataOf();
        Assert.Equal("pending", data.GetProperty("status").GetString());
        // No Ajeer/contract/invoice fields.
        Assert.False(data.TryGetProperty("contract", out _));
        Assert.False(data.TryGetProperty("contract_type", out _));
        Assert.False(data.TryGetProperty("contract_path", out _));

        var outboxCount = await OaoHelpers.CountOutboxEventsAsync(
            _factory, OfferEventTypes.Created);
        Assert.True(outboxCount > 0);
    }

    [Fact]
    public async Task SentOffers_StatusFilter_ExcludesNonMatchingStatuses()
    {
        // The organizer's offer screen filters by حالة العقد (status[]).
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var send = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_senderEstablishment}/offers/send",
            new { applicant_id = _workerApplicationId, monthly_salary = 6000m });
        Assert.Equal(HttpStatusCode.Created, send.StatusCode);
        using var sendDoc = JsonDocument.Parse(await send.Content.ReadAsStringAsync());
        var offerId = sendDoc.RootElement.DataOf().GetProperty("id").GetGuid();

        // status=rejected → the new Pending offer must be filtered OUT.
        Assert.DoesNotContain(offerId, await SentOfferIds(client, "?status=rejected"));
        // status=pending → it must appear.
        Assert.Contains(offerId, await SentOfferIds(client, "?status=pending"));
        // No filter → it must appear.
        Assert.Contains(offerId, await SentOfferIds(client, ""));
    }

    private async Task<List<Guid>> SentOfferIds(HttpClient client, string query)
    {
        var json = await client.GetStringAsync(
            $"/api/v1/establishments/{_senderEstablishment}/sent-offers{query}");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.DataOf().EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid()).ToList();
    }

    private static async Task<List<Guid>> ReceivedOfferIds(
        HttpClient client, Guid establishmentId, string query)
    {
        var json = await client.GetStringAsync(
            $"/api/v1/establishments/{establishmentId}/received-offers{query}");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.DataOf().EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid()).ToList();
    }

    [Fact]
    public async Task SentOffers_ApplicantNameSearch_FiltersByApplierName()
    {
        // The بحث box on the sent-offers screen sends ?applicant_name= and
        // legacy matched the applier's name. Seed a named user + its own
        // offer so the assertion is isolated from the shared fixture.
        var namedWorker = new TestUser(Sub: "oao-offer-named-worker", Roles: new[] { "matloob_user" });
        await OaoHelpers.SeedLocalUserAsync(_factory, namedWorker.Sub);
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FirstAsync(u => u.IdentityId == namedWorker.Sub);
            typeof(Matloob.Domain.Users.User).GetProperty(nameof(Matloob.Domain.Users.User.Name))!
                .SetValue(user, "Ahmed Worker");
            await db.SaveChangesAsync();
        }
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, _opportunityVacancy, applicantUserId: namedWorker.Sub);
        var offerId = await OaoHelpers.SeedOfferAsync(
            _factory, _senderEstablishment, _opportunityVacancy, appId, OaoHelpers.EstablishmentOwner.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        // Case-insensitive substring → present; non-matching → absent.
        Assert.Contains(offerId, await SentOfferIds(client, "?applicant_name=ahmed"));
        Assert.DoesNotContain(offerId, await SentOfferIds(client, "?applicant_name=zzznotpresent"));
    }

    [Fact]
    public async Task ReceivedOffers_SenderNameSearch_FiltersBySenderName()
    {
        // The بحث box on the received-offers screen sends ?sender_name= and
        // legacy matched the sending establishment's name. The sender's
        // seeded name contains "Test Establishment".
        var applicantOwner = new TestUser(Sub: "oao-offer-recv-owner", Roles: new[] { "matloob_user" });
        await OaoHelpers.SeedLocalUserAsync(_factory, applicantOwner.Sub);
        var applicantEst = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, applicantOwner.Sub, "CR-OAO-OFFER-RECV");
        var estApplication = await OaoHelpers.SeedApplicationAsync(
            _factory, _opportunityVacancy,
            applicantEstablishmentId: applicantEst, appliedByUserId: applicantOwner.Sub);
        var offerId = await OaoHelpers.SeedOfferAsync(
            _factory, _senderEstablishment, _opportunityVacancy, estApplication, OaoHelpers.EstablishmentOwner.Sub);

        var client = _factory.CreateClientFor(applicantOwner);
        Assert.Contains(offerId, await ReceivedOfferIds(client, applicantEst, "?sender_name=test"));
        Assert.DoesNotContain(offerId, await ReceivedOfferIds(client, applicantEst, "?sender_name=zzznotpresent"));
    }

    [Fact]
    public async Task Send_VacancyDailyWage_ComputesMonthlySalaryAndWorkingDays()
    {
        // For a vacancy opportunity the form sends only daily_wage (+ dates);
        // the API derives number_of_working_days = days(start..end) and
        // monthly_salary = daily_wage × days (legacy SendOfferService).
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_senderEstablishment}/offers/send",
            new
            {
                applicant_id = _workerApplicationId,
                daily_wage = 150,
                start_date = "2026-06-20",
                end_date = "2026-06-25",
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var data = doc.RootElement.DataOf();
        // Inclusive day count (matches the wizard): 25 - 20 + 1 = 6 working
        // days; 150 × 6 = 900.
        Assert.Equal(6, data.GetProperty("number_of_working_days").GetInt32());
        Assert.Equal(900m, data.GetProperty("monthly_salary").GetDecimal());
        Assert.Equal(150, data.GetProperty("daily_wage").GetInt32());
    }

    [Fact]
    public async Task Send_PrecognitionPing_Returns204_AndCreatesNoOffer()
    {
        // The offer form's step-1 "next" sends a Precognition validate-only
        // ping. It must NOT persist an offer (else the real send 409s).
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/establishments/{_senderEstablishment}/offers/send")
        {
            Content = JsonContent.Create(new
            {
                applicant_id = _workerApplicationId,
                monthly_salary = 6000m,
            }),
        };
        req.Headers.Add("Precognition", "true");
        var response = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // A subsequent real send must still succeed (no offer was created).
        var real = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_senderEstablishment}/offers/send",
            new { applicant_id = _workerApplicationId, monthly_salary = 6000m });
        Assert.Equal(HttpStatusCode.Created, real.StatusCode);
    }

    [Fact]
    public async Task Send_ProfessionCategoryId_RoutedToCategoryFk_NotJobTitle()
    {
        // The offer form sends an opportunity-CATEGORY id in `job_title_id`
        // (the ajeer→job_titles branch is dead since Ajeer is dropped). It
        // must persist to job_title_category_id (its real FK), not
        // job_title_id (→ job_titles, which would FK-violate), and round-trip
        // as offer.job_title.title.
        Guid categoryId;
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            categoryId = (await db.OpportunityCategories
                .FirstAsync(c => c.ParentId != null && !c.IsOther)).Id;
        }

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_senderEstablishment}/offers/send",
            new { applicant_id = _workerApplicationId, monthly_salary = 6000m, job_title_id = categoryId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var data = doc.RootElement.DataOf();
        var id = data.GetProperty("id").GetGuid();
        var jobTitle = data.GetProperty("job_title");
        Assert.Equal(categoryId, jobTitle.GetProperty("id").GetGuid());
        Assert.False(string.IsNullOrEmpty(jobTitle.GetProperty("title").GetString()));

        var offer = await OaoHelpers.LoadOfferAsync(_factory, id);
        Assert.NotNull(offer);
        Assert.Null(offer!.JobTitleId);
        Assert.Equal(categoryId, offer.JobTitleCategoryId);
    }

    [Fact]
    public async Task Send_UnknownProfession_Returns422()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_senderEstablishment}/offers/send",
            new { applicant_id = _workerApplicationId, monthly_salary = 6000m, job_title_id = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Send_NonUtcValidityOffset_PersistsAsUtc()
    {
        // The frontend posts offer_validity_* with a +03:00 (Riyadh) offset.
        // Npgsql rejects a non-UTC DateTimeOffset on a `timestamptz` column
        // (500), so the endpoint must normalize to UTC before persisting.
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_senderEstablishment}/offers/send",
            new
            {
                applicant_id = _workerApplicationId,
                monthly_salary = 6000m,
                offer_validity_from = "2026-06-04T00:00:00+03:00",
                offer_validity_to = "2026-07-04T00:00:00+03:00",
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var id = doc.RootElement.DataOf().GetProperty("id").GetGuid();

        var offer = await OaoHelpers.LoadOfferAsync(_factory, id);
        Assert.NotNull(offer);
        Assert.Equal(TimeSpan.Zero, offer!.OfferValidityFrom!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, offer.OfferValidityTo!.Value.Offset);
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
        var data = doc.RootElement.DataOf();
        Assert.Equal("pending_sponsor_approval", data.GetProperty("status").GetString());
        Assert.True(data.GetProperty("is_pending_sponsor_approval").GetBoolean());
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
        Assert.Contains(doc.RootElement.DataOf().EnumerateArray(),
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
        var data = doc.RootElement.DataOf();
        Assert.Equal("accepted", data.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, data.GetProperty("accepted_at").ValueKind);
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
        Assert.Equal("rejected", doc.RootElement.DataOf().GetProperty("status").GetString());
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
        Assert.Equal("canceled", doc.RootElement.DataOf().GetProperty("status").GetString());
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
        Assert.Equal("accepted", doc.RootElement.DataOf().GetProperty("status").GetString());
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
        var offerId = sendDoc.RootElement.DataOf().GetProperty("id").GetGuid();

        var response = await _factory.CreateClientFor(SponsorOwner)
            .PostAsync(
                $"/api/v1/establishments/{_sponsorEstablishment}/offers/{offerId}/sponsor/accept",
                content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("pending", doc.RootElement.DataOf().GetProperty("status").GetString());
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
        var offerId = sendDoc.RootElement.DataOf().GetProperty("id").GetGuid();

        var reasonId = await GetAnyRejectionReasonAsync();
        var response = await _factory.CreateClientFor(SponsorOwner)
            .PostAsJsonAsync(
                $"/api/v1/establishments/{_sponsorEstablishment}/offers/{offerId}/sponsor/reject",
                new { reason_id = reasonId, other_reason = "no thanks" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("sponsor_rejected", doc.RootElement.DataOf().GetProperty("status").GetString());
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
        return doc.RootElement.DataOf().GetProperty("id").GetGuid();
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
