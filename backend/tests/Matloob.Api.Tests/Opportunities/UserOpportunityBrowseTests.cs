using System.Net;
using System.Text.Json;
using Matloob.Domain.Opportunities;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// Tests for the user-side opportunity browse endpoints
/// (<c>GET /api/users/opportunities</c> + canonical alias and
/// <c>GET /api/users/opportunities/{id}</c> + canonical alias).
/// </summary>
public sealed class UserOpportunityBrowseTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;
    private Guid _establishmentId;

    public UserOpportunityBrowseTests(OpportunitiesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker2.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);

        _establishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-OAO-USER-BROWSE");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -- list ---------------------------------------------------------------

    [Fact]
    public async Task List_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var response = await anon.GetAsync("/api/users/opportunities");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_Authenticated_ReturnsLaravelShape()
    {
        await OaoHelpers.SeedOpportunityAsync(
            _factory, _establishmentId, name: "Servers Needed",
            forVacancy: true,
            status: OpportunityStatus.Upcoming);

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync("/api/users/opportunities");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        var first = doc.RootElement.EnumerateArray()
            .First(e => e.GetProperty("name").GetString() == "Servers Needed");

        // Laravel snake_case key presence.
        Assert.True(first.TryGetProperty("start_date", out _));
        Assert.True(first.TryGetProperty("end_date", out _));
        Assert.True(first.TryGetProperty("location_title", out _));
        Assert.True(first.TryGetProperty("required_personnel", out _));
        Assert.True(first.TryGetProperty("opportunity_category", out _));
        Assert.True(first.TryGetProperty("is_applied", out _));
        Assert.True(first.TryGetProperty("applicants_count", out _));
        Assert.True(first.TryGetProperty("can_end", out _));

        // No Ajeer / contract / invoice fields leaked through.
        Assert.False(first.TryGetProperty("contracts_count", out _));
        Assert.False(first.TryGetProperty("contract", out _));
        Assert.False(first.TryGetProperty("ajeer_contract_number", out _));
    }

    [Fact]
    public async Task List_LegacyAndCanonical_ReturnSamePayload()
    {
        await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "Dual route");

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var legacy = await client.GetStringAsync("/api/users/opportunities");
        var canonical = await client.GetStringAsync("/api/v1/users/opportunities");

        Assert.Equal(legacy, canonical);
    }

    [Fact]
    public async Task List_ExcludesNonBrowsableStatuses()
    {
        await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "Drafted opp",
            status: OpportunityStatus.Drafted);
        await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "Ended opp",
            status: OpportunityStatus.Ended);
        await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "Upcoming opp",
            status: OpportunityStatus.Upcoming);

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync("/api/users/opportunities");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        var names = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!)
            .ToList();

        Assert.Contains("Upcoming opp", names);
        Assert.DoesNotContain("Drafted opp", names);
        Assert.DoesNotContain("Ended opp", names);
    }

    [Fact]
    public async Task List_ExcludesEstablishmentOpportunityCategories()
    {
        // Establishment-side categories have for_vacancy = false.
        await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "For establishments",
            forVacancy: false,
            status: OpportunityStatus.Upcoming);

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync("/api/users/opportunities");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        Assert.DoesNotContain(doc.RootElement.EnumerateArray(),
            e => e.GetProperty("name").GetString() == "For establishments");
    }

    [Fact]
    public async Task List_IsApplied_TrueWhenUserHasApplication()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "Applied opp");
        await OaoHelpers.SeedApplicationAsync(_factory, oppId,
            applicantUserId: OaoHelpers.Worker.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync("/api/users/opportunities");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        var mine = doc.RootElement.EnumerateArray()
            .First(e => e.GetProperty("name").GetString() == "Applied opp");
        Assert.True(mine.GetProperty("is_applied").GetBoolean());
    }

    [Fact]
    public async Task List_IsApplied_FalseWhenAnotherUserApplied()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "Other applied");
        await OaoHelpers.SeedApplicationAsync(_factory, oppId,
            applicantUserId: OaoHelpers.Worker2.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync("/api/users/opportunities");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        var other = doc.RootElement.EnumerateArray()
            .First(e => e.GetProperty("name").GetString() == "Other applied");
        Assert.False(other.GetProperty("is_applied").GetBoolean());
    }

    // -- show ---------------------------------------------------------------

    [Fact]
    public async Task Show_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var response = await anon.GetAsync($"/api/users/opportunities/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Show_UnknownId_Returns404()
    {
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync($"/api/users/opportunities/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_KnownVacancyOpportunity_ReturnsLaravelShape()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "Detail target");

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync($"/api/users/opportunities/{oppId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal(oppId, doc.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Detail target", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Show_NonBrowsableStatus_Returns404()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "Drafted",
            status: OpportunityStatus.Drafted);

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync($"/api/users/opportunities/{oppId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_EstablishmentCategoryOpportunity_Returns404()
    {
        // For-establishments opportunities aren't visible on the worker route.
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "Org-side",
            forVacancy: false,
            status: OpportunityStatus.Upcoming);

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync($"/api/users/opportunities/{oppId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
