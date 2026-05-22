using System.Net;
using System.Text.Json;
using Matloob.Domain.Opportunities;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// Tests for the owner-side opportunity read endpoints
/// (<c>GET /api/establishments/me/opportunities</c> + <c>/{id}</c>)
/// and the canonical
/// <c>/api/v1/establishments/{establishmentId}/opportunities/...</c>
/// aliases.
/// </summary>
public sealed class MineOpportunityTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;
    private Guid _ownEstablishment;
    private Guid _otherEstablishment;

    private static readonly Auth.TestUser OtherOwner = new(
        Sub: "oao-other-owner-1",
        Roles: new[] { "matloob_user" });

    public MineOpportunityTests(OpportunitiesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OtherOwner.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Outsider.Sub);

        _ownEstablishment = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-OAO-MINE-A");
        _otherEstablishment = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OtherOwner.Sub, "CR-OAO-MINE-B");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var response = await anon.GetAsync("/api/establishments/me/opportunities");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_NonMember_Returns404()
    {
        var outsider = _factory.CreateClientFor(OaoHelpers.Outsider);
        var response = await outsider.GetAsync(
            $"/api/v1/establishments/{_ownEstablishment}/opportunities");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_Owner_SeesAllOwnStatuses_HidesOthers()
    {
        await OaoHelpers.SeedOpportunityAsync(_factory, _ownEstablishment,
            name: "Own draft", status: OpportunityStatus.Drafted);
        await OaoHelpers.SeedOpportunityAsync(_factory, _ownEstablishment,
            name: "Own upcoming", status: OpportunityStatus.Upcoming);
        await OaoHelpers.SeedOpportunityAsync(_factory, _ownEstablishment,
            name: "Own ended", status: OpportunityStatus.Ended);
        await OaoHelpers.SeedOpportunityAsync(_factory, _otherEstablishment,
            name: "Foreign", status: OpportunityStatus.Active);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_ownEstablishment}/opportunities");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        var names = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!).ToList();

        Assert.Contains("Own draft", names);
        Assert.Contains("Own upcoming", names);
        Assert.Contains("Own ended", names);
        Assert.DoesNotContain("Foreign", names);
    }

    [Fact]
    public async Task List_LegacyRoute_ResolvesByHeader()
    {
        await OaoHelpers.SeedOpportunityAsync(_factory, _ownEstablishment,
            name: "Header resolved");

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/establishments/me/opportunities");
        req.Headers.Add("X-Establishment-Id", _ownEstablishment.ToString());
        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task List_StatusFilter_Honoured()
    {
        await OaoHelpers.SeedOpportunityAsync(_factory, _ownEstablishment,
            name: "F-upcoming", status: OpportunityStatus.Upcoming);
        await OaoHelpers.SeedOpportunityAsync(_factory, _ownEstablishment,
            name: "F-ended", status: OpportunityStatus.Ended);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_ownEstablishment}/opportunities?status=Ended");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        var names = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!).ToList();
        Assert.Contains("F-ended", names);
        Assert.DoesNotContain("F-upcoming", names);
    }

    [Fact]
    public async Task Show_Owner_ReturnsOpportunity()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _ownEstablishment,
            name: "Detail", status: OpportunityStatus.Upcoming);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_ownEstablishment}/opportunities/{oppId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Show_DifferentEstablishment_Returns404()
    {
        var foreignOpp = await OaoHelpers.SeedOpportunityAsync(_factory, _otherEstablishment,
            name: "Foreign detail", status: OpportunityStatus.Upcoming);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_ownEstablishment}/opportunities/{foreignOpp}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_OwnerSeesDraftedOpportunity()
    {
        // Drafted is hidden from public browse but visible to the owner.
        var draft = await OaoHelpers.SeedOpportunityAsync(_factory, _ownEstablishment,
            name: "Draft detail", status: OpportunityStatus.Drafted);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_ownEstablishment}/opportunities/{draft}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("Drafted", doc.RootElement.GetProperty("status").GetString());
    }
}
