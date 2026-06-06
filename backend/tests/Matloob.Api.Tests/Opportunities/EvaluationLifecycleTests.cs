using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Common;
using Matloob.Domain.Offers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Opportunities;

public sealed class EvaluationLifecycleTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;
    private Guid _senderEstablishment;
    private Guid _opportunityId;
    private Guid _applicationId;
    private Guid _offerId;

    public EvaluationLifecycleTests(OpportunitiesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);

        _senderEstablishment = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-OAO-EVAL");
        _opportunityId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _senderEstablishment,
            name: "Eval opp", forVacancy: true);
        _applicationId = await OaoHelpers.SeedApplicationAsync(
            _factory, _opportunityId,
            applicantUserId: OaoHelpers.Worker.Sub);
        _offerId = await OaoHelpers.SeedOfferAsync(
            _factory, _senderEstablishment, _opportunityId, _applicationId,
            sentByUserId: OaoHelpers.EstablishmentOwner.Sub,
            status: OfferStatus.Accepted,
            acceptedAt: DateTimeOffset.UtcNow);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UserCreate_HappyPath_Returns201()
    {
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.PostAsJsonAsync("/api/v1/users/evaluations",
            new
            {
                offer_id = _offerId,
                rating = 5,
                recommend_for_future_opportunities = true,
                comment = "great experience",
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var data = doc.RootElement.DataOf();
        Assert.Equal(_offerId, data.GetProperty("offer_id").GetGuid());
        Assert.Equal("user", data.GetProperty("evaluator").GetProperty("type").GetString());
        Assert.Equal("organization", data.GetProperty("evaluable").GetProperty("type").GetString());
        // No "contract" field anywhere.
        Assert.False(data.TryGetProperty("contract", out _));
    }

    [Fact]
    public async Task UserCreate_Duplicate_Returns409()
    {
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var body = new
        {
            offer_id = _offerId,
            rating = 5,
            recommend_for_future_opportunities = true,
        };
        var first = await client.PostAsJsonAsync("/api/v1/users/evaluations", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await client.PostAsJsonAsync("/api/v1/users/evaluations", body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task UserCreate_OfferNotEvaluable_Returns422()
    {
        // Offer in Pending state isn't evaluable.
        var pendingOffer = await OaoHelpers.SeedOfferAsync(
            _factory, _senderEstablishment, _opportunityId, _applicationId,
            sentByUserId: OaoHelpers.EstablishmentOwner.Sub,
            status: OfferStatus.Pending);
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.PostAsJsonAsync("/api/v1/users/evaluations",
            new
            {
                offer_id = pendingOffer,
                rating = 3,
                recommend_for_future_opportunities = false,
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task BothSides_Evaluate_OfferBecomesCompleted()
    {
        // Worker evaluates first.
        await _factory.CreateClientFor(OaoHelpers.Worker).PostAsJsonAsync(
            "/api/v1/users/evaluations",
            new
            {
                offer_id = _offerId,
                rating = 5,
                recommend_for_future_opportunities = true,
            });

        // Establishment evaluates second.
        var response = await _factory.CreateClientFor(OaoHelpers.EstablishmentOwner)
            .PostAsJsonAsync(
                $"/api/v1/establishments/{_senderEstablishment}/evaluations",
                new
                {
                    offer_id = _offerId,
                    rating = 5,
                    recommend_for_future_opportunities = true,
                });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var offer = await OaoHelpers.LoadOfferAsync(_factory, _offerId);
        Assert.Equal(OfferStatus.Completed, offer!.Status);
    }

    [Fact]
    public async Task OtherEvaluation_NotEvaluatedYourself_Returns422()
    {
        // The establishment evaluates but the worker has not.
        await _factory.CreateClientFor(OaoHelpers.EstablishmentOwner)
            .PostAsJsonAsync(
                $"/api/v1/establishments/{_senderEstablishment}/evaluations",
                new { offer_id = _offerId, rating = 5, recommend_for_future_opportunities = true });

        var response = await _factory.CreateClientFor(OaoHelpers.Worker)
            .GetAsync($"/api/v1/users/offers/{_offerId}/other-evaluation");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task OtherEvaluation_AfterBothEvaluated_Returns200()
    {
        await _factory.CreateClientFor(OaoHelpers.Worker)
            .PostAsJsonAsync("/api/v1/users/evaluations",
                new { offer_id = _offerId, rating = 4, recommend_for_future_opportunities = true });
        await _factory.CreateClientFor(OaoHelpers.EstablishmentOwner)
            .PostAsJsonAsync(
                $"/api/v1/establishments/{_senderEstablishment}/evaluations",
                new { offer_id = _offerId, rating = 5, recommend_for_future_opportunities = true });

        // Worker sees the establishment's evaluation.
        var response = await _factory.CreateClientFor(OaoHelpers.Worker)
            .GetAsync($"/api/v1/users/offers/{_offerId}/other-evaluation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("organization", doc.RootElement.DataOf().GetProperty("evaluator").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Unevaluated_ShowsAcceptedOffersAwaitingEvaluation()
    {
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync("/api/v1/users/offers/unevaluated");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Contains(doc.RootElement.DataOf().EnumerateArray(),
            e => e.GetProperty("id").GetGuid() == _offerId);
    }

    [Fact]
    public async Task Unevaluated_HidesAfterUserEvaluates()
    {
        // Worker evaluates the offer.
        await _factory.CreateClientFor(OaoHelpers.Worker)
            .PostAsJsonAsync("/api/v1/users/evaluations",
                new { offer_id = _offerId, rating = 5, recommend_for_future_opportunities = true });

        var response = await _factory.CreateClientFor(OaoHelpers.Worker)
            .GetAsync("/api/v1/users/offers/unevaluated");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.DoesNotContain(doc.RootElement.DataOf().EnumerateArray(),
            e => e.GetProperty("id").GetGuid() == _offerId);
    }

    [Fact]
    public async Task UserOfferRead_BeforeEvaluation_EvaluatedFalse()
    {
        var response = await _factory.CreateClientFor(OaoHelpers.Worker)
            .GetAsync($"/api/v1/users/offers/{_offerId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.False(doc.RootElement.DataOf().GetProperty("evaluated").GetBoolean());
    }

    [Fact]
    public async Task UserOfferRead_AfterWorkerEvaluates_EvaluatedTrue()
    {
        await _factory.CreateClientFor(OaoHelpers.Worker)
            .PostAsJsonAsync("/api/v1/users/evaluations",
                new { offer_id = _offerId, rating = 5, recommend_for_future_opportunities = true });

        var response = await _factory.CreateClientFor(OaoHelpers.Worker)
            .GetAsync($"/api/v1/users/offers/{_offerId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.True(doc.RootElement.DataOf().GetProperty("evaluated").GetBoolean());
    }

    [Fact]
    public async Task OfferRead_EvaluatedIsViewerSpecific()
    {
        // The worker evaluates — that is the OTHER side from the establishment's
        // perspective, so the establishment's own `evaluated` must stay false until
        // the establishment itself posts. Guards against an "any evaluation exists"
        // regression that would prematurely clear the counterparty's prompt.
        await _factory.CreateClientFor(OaoHelpers.Worker)
            .PostAsJsonAsync("/api/v1/users/evaluations",
                new { offer_id = _offerId, rating = 5, recommend_for_future_opportunities = true });

        var owner = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);

        var before = await owner.GetAsync(
            $"/api/v1/establishments/{_senderEstablishment}/sent-offers/{_offerId}");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        await using (var s1 = await before.Content.ReadAsStreamAsync())
        using (var d1 = await JsonDocument.ParseAsync(s1))
            Assert.False(d1.RootElement.DataOf().GetProperty("evaluated").GetBoolean());

        // Now the establishment posts its own evaluation — its flag flips to true.
        await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{_senderEstablishment}/evaluations",
            new { offer_id = _offerId, rating = 5, recommend_for_future_opportunities = true });

        var after = await owner.GetAsync(
            $"/api/v1/establishments/{_senderEstablishment}/sent-offers/{_offerId}");
        await using (var s2 = await after.Content.ReadAsStreamAsync())
        using (var d2 = await JsonDocument.ParseAsync(s2))
            Assert.True(d2.RootElement.DataOf().GetProperty("evaluated").GetBoolean());
    }
}
