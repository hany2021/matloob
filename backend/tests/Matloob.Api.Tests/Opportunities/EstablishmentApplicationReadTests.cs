using System.Net;
using System.Text.Json;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// Tests for the four establishment-side application read endpoints:
/// browse list / browse show, plus own-opportunity applicants list /
/// own-opportunity applicant show.
/// </summary>
public sealed class EstablishmentApplicationReadTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;

    private Guid _ownEstablishment;
    private Guid _publisherEstablishment;

    private static readonly Auth.TestUser Publisher = new(
        Sub: "oao-est-app-publisher",
        Roles: new[] { "matloob_user" });

    public EstablishmentApplicationReadTests(OpportunitiesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Outsider.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, Publisher.Sub);

        _ownEstablishment = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-OAO-EST-APP-A");
        _publisherEstablishment = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, Publisher.Sub, "CR-OAO-EST-APP-B");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -- browse: applications submitted BY the establishment ----------------

    [Fact]
    public async Task BrowseList_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var response = await anon.GetAsync("/api/establishments/opportunities/applications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BrowseList_OnlyEstablishmentOwnApplications()
    {
        var oppA = await OaoHelpers.SeedOpportunityAsync(_factory, _publisherEstablishment,
            name: "Pub A", forVacancy: false);
        var oppB = await OaoHelpers.SeedOpportunityAsync(_factory, _publisherEstablishment,
            name: "Pub B", forVacancy: false);
        await OaoHelpers.SeedApplicationAsync(_factory, oppA,
            applicantEstablishmentId: _ownEstablishment,
            appliedByUserId: OaoHelpers.EstablishmentOwner.Sub);
        // Application from another establishment we should not see.
        await OaoHelpers.SeedApplicationAsync(_factory, oppB,
            applicantEstablishmentId: _publisherEstablishment,
            appliedByUserId: Publisher.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_ownEstablishment}/browse/applications");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        var oppNames = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("opportunity").GetProperty("name").GetString())
            .ToList();
        Assert.Contains("Pub A", oppNames);
        Assert.DoesNotContain("Pub B", oppNames);
    }

    [Fact]
    public async Task BrowseShow_OtherEstablishmentsApplication_Returns404()
    {
        var opp = await OaoHelpers.SeedOpportunityAsync(_factory, _publisherEstablishment,
            name: "Foreign", forVacancy: false);
        var foreignApp = await OaoHelpers.SeedApplicationAsync(_factory, opp,
            applicantEstablishmentId: _publisherEstablishment,
            appliedByUserId: Publisher.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_ownEstablishment}/browse/applications/{foreignApp}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BrowseShow_OwnApplication_Returns200_WithOrganizationApplierType()
    {
        var opp = await OaoHelpers.SeedOpportunityAsync(_factory, _publisherEstablishment,
            name: "Detail", forVacancy: false);
        var appId = await OaoHelpers.SeedApplicationAsync(_factory, opp,
            applicantEstablishmentId: _ownEstablishment,
            appliedByUserId: OaoHelpers.EstablishmentOwner.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_ownEstablishment}/browse/applications/{appId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("organization", doc.RootElement.GetProperty("applier_type").GetString());
        Assert.True(doc.RootElement.TryGetProperty("applied_by", out _));
    }

    // -- own-opportunity applicants list ------------------------------------

    [Fact]
    public async Task MineApplicants_List_OnlyApplicantsOnOwnOpportunity()
    {
        var ownOpp = await OaoHelpers.SeedOpportunityAsync(_factory, _ownEstablishment,
            name: "Ours");
        var otherOpp = await OaoHelpers.SeedOpportunityAsync(_factory, _publisherEstablishment,
            name: "Theirs");

        await OaoHelpers.SeedApplicationAsync(_factory, ownOpp, applicantUserId: OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedApplicationAsync(_factory, otherOpp, applicantUserId: OaoHelpers.Worker.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_ownEstablishment}/opportunities/{ownOpp}/applications");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.True(doc.RootElement.GetArrayLength() >= 1);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            // Every row's opportunity must be the one in the path.
            Assert.Equal(ownOpp,
                item.GetProperty("opportunity").GetProperty("id").GetGuid());
        }
    }

    [Fact]
    public async Task MineApplicants_List_ForeignOpportunity_Returns404()
    {
        var foreignOpp = await OaoHelpers.SeedOpportunityAsync(_factory, _publisherEstablishment,
            name: "Foreign");
        await OaoHelpers.SeedApplicationAsync(_factory, foreignOpp,
            applicantUserId: OaoHelpers.Worker.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_ownEstablishment}/opportunities/{foreignOpp}/applications");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -- own-opportunity applicant show -------------------------------------

    [Fact]
    public async Task MineApplicant_Show_OwnOpportunity_Returns200()
    {
        var opp = await OaoHelpers.SeedOpportunityAsync(_factory, _ownEstablishment,
            name: "Targeted");
        var app = await OaoHelpers.SeedApplicationAsync(_factory, opp,
            applicantUserId: OaoHelpers.Worker.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_ownEstablishment}/applicants/{app}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MineApplicant_Show_ForeignOpportunity_Returns404()
    {
        var foreignOpp = await OaoHelpers.SeedOpportunityAsync(_factory, _publisherEstablishment,
            name: "Foreign");
        var foreignApp = await OaoHelpers.SeedApplicationAsync(_factory, foreignOpp,
            applicantUserId: OaoHelpers.Worker.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_ownEstablishment}/applicants/{foreignApp}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MineApplicants_LegacyAndCanonical_ReturnSamePayload()
    {
        var opp = await OaoHelpers.SeedOpportunityAsync(_factory, _ownEstablishment,
            name: "Dual route opp");
        await OaoHelpers.SeedApplicationAsync(_factory, opp,
            applicantUserId: OaoHelpers.Worker.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        // Legacy needs context resolution; use query param.
        var legacy = await client.GetStringAsync(
            $"/api/establishments/me/opportunities/{opp}/applications?establishment_id={_ownEstablishment}");
        var canonical = await client.GetStringAsync(
            $"/api/v1/establishments/{_ownEstablishment}/opportunities/{opp}/applications");
        Assert.Equal(legacy, canonical);
    }
}
