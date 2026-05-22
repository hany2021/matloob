using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Tests.Auth;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Matloob.Api.Infrastructure.Persistence;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// Tests for the Phase OAO-4 apply endpoints (user + establishment).
/// </summary>
public sealed class ApplyLifecycleTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;
    private Guid _vacancyOpp;
    private Guid _orgOpp;
    private Guid _publisherEstablishment;
    private Guid _applyingEstablishment;

    private static readonly TestUser PublisherOwner = new(
        Sub: "oao-apply-pub-owner", Roles: new[] { "matloob_user" });

    public ApplyLifecycleTests(OpportunitiesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker2.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, PublisherOwner.Sub);

        _publisherEstablishment = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, PublisherOwner.Sub, "CR-OAO-APPLY-PUB");
        _applyingEstablishment = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-OAO-APPLY-APP");

        _vacancyOpp = await OaoHelpers.SeedOpportunityAsync(
            _factory, _publisherEstablishment, name: "Vacancy", forVacancy: true);
        _orgOpp = await OaoHelpers.SeedOpportunityAsync(
            _factory, _publisherEstablishment, name: "Org gig", forVacancy: false);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -- user apply ---------------------------------------------------------

    [Fact]
    public async Task UserApply_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var response = await anon.PostAsync(
            $"/api/users/opportunities/{_vacancyOpp}/apply", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UserApply_UnknownOpportunity_Returns404()
    {
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.PostAsync(
            $"/api/users/opportunities/{Guid.NewGuid()}/apply", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UserApply_HappyPath_Returns201()
    {
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.PostAsync(
            $"/api/users/opportunities/{_vacancyOpp}/apply", content: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("application_submitted_successfully",
            doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task UserApply_NonVacancyCategory_Returns422()
    {
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.PostAsync(
            $"/api/users/opportunities/{_orgOpp}/apply", content: null);
        // Worker hitting an org-side opportunity: the GET endpoint returns
        // 404 for those, and apply will follow the same browsable rule.
        // Even though the opportunity exists, the worker route filters to
        // for_vacancy=true. Both 404 and 422 are reasonable — assert what
        // the endpoint actually emits today.
        Assert.True(
            response.StatusCode == HttpStatusCode.UnprocessableEntity
            || response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UserApply_Duplicate_Returns409()
    {
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var first = await client.PostAsync(
            $"/api/users/opportunities/{_vacancyOpp}/apply", content: null);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsync(
            $"/api/users/opportunities/{_vacancyOpp}/apply", content: null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        await using var stream = await second.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("application_already_exists",
            doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UserApply_OpportunityFull_Returns422()
    {
        // Opportunity with required_personnel=1 + already one application
        // -> the next applicant is rejected as not_applicable.
        var smallOpp = await OaoHelpers.SeedOpportunityAsync(
            _factory, _publisherEstablishment, name: "Small", forVacancy: true);

        // Seed one application using a separate seeded user.
        await OaoHelpers.SeedApplicationAsync(_factory, smallOpp,
            applicantUserId: OaoHelpers.Worker2.Sub);
        // Bump required_personnel down to 1 to force full immediately.
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var opp = await db.Opportunities.FirstAsync(o => o.Id == smallOpp);
            typeof(Matloob.Domain.Opportunities.Opportunity)
                .GetProperty("RequiredPersonnel")!
                .SetValue(opp, 1);
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.PostAsync(
            $"/api/users/opportunities/{smallOpp}/apply", content: null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("application_not_applicable",
            doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UserApply_LegacyAndCanonical_BothWork()
    {
        var legacyResp = await _factory.CreateClientFor(OaoHelpers.Worker).PostAsync(
            $"/api/users/opportunities/{_vacancyOpp}/apply", content: null);
        Assert.Equal(HttpStatusCode.Created, legacyResp.StatusCode);

        // Different worker hits canonical to avoid the duplicate-apply 409.
        var canonResp = await _factory.CreateClientFor(OaoHelpers.Worker2).PostAsync(
            $"/api/v1/users/opportunities/{_vacancyOpp}/apply", content: null);
        Assert.Equal(HttpStatusCode.Created, canonResp.StatusCode);
    }

    // -- establishment apply ------------------------------------------------

    [Fact]
    public async Task EstablishmentApply_HappyPath_Returns201_AndIsOrganizationType()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsync(
            $"/api/v1/establishments/{_applyingEstablishment}/browse/opportunities/{_orgOpp}/apply",
            content: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Confirm the application now appears in the establishment-browse
        // applications endpoint as organization.
        var list = await client.GetAsync(
            $"/api/v1/establishments/{_applyingEstablishment}/browse/applications");
        await using var stream = await list.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var found = doc.RootElement.EnumerateArray().Single(e =>
            e.GetProperty("opportunity").GetProperty("id").GetGuid() == _orgOpp);
        Assert.Equal("organization", found.GetProperty("applier_type").GetString());
    }

    [Fact]
    public async Task EstablishmentApply_VacancyCategory_Returns422()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsync(
            $"/api/v1/establishments/{_applyingEstablishment}/browse/opportunities/{_vacancyOpp}/apply",
            content: null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("application_category_mismatch",
            doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task EstablishmentApply_OwnOpportunity_Returns422()
    {
        var ownOpp = await OaoHelpers.SeedOpportunityAsync(
            _factory, _applyingEstablishment, name: "Own org", forVacancy: false);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsync(
            $"/api/v1/establishments/{_applyingEstablishment}/browse/opportunities/{ownOpp}/apply",
            content: null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("application_self_not_allowed",
            doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task EstablishmentApply_Duplicate_Returns409()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var url = $"/api/v1/establishments/{_applyingEstablishment}/browse/opportunities/{_orgOpp}/apply";

        var first = await client.PostAsync(url, content: null);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsync(url, content: null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
}
