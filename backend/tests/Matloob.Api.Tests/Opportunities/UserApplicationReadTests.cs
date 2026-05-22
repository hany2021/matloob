using System.Net;
using System.Text.Json;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// Tests for the user-side application read endpoints
/// (<c>GET /api/users/opportunities/applications</c> and
/// <c>/{applicantId}</c>) and the canonical
/// <c>/api/v1/users/opportunities/applications/...</c> aliases.
/// </summary>
public sealed class UserApplicationReadTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;
    private Guid _establishmentId;

    public UserApplicationReadTests(OpportunitiesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker2.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);

        _establishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-OAO-USER-APP");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var response = await anon.GetAsync("/api/users/opportunities/applications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsOnlyOwnApplications()
    {
        var opp1 = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId, name: "App-self");
        var opp2 = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId, name: "App-other");
        await OaoHelpers.SeedApplicationAsync(_factory, opp1, applicantUserId: OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedApplicationAsync(_factory, opp2, applicantUserId: OaoHelpers.Worker2.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync("/api/users/opportunities/applications");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        var oppNames = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("opportunity").GetProperty("name").GetString())
            .ToList();
        Assert.Contains("App-self", oppNames);
        Assert.DoesNotContain("App-other", oppNames);
    }

    [Fact]
    public async Task List_ReturnsLaravelShape()
    {
        var opp = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId, name: "Shape opp");
        await OaoHelpers.SeedApplicationAsync(_factory, opp, applicantUserId: OaoHelpers.Worker.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync("/api/users/opportunities/applications");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        var first = doc.RootElement.EnumerateArray().First();
        Assert.True(first.TryGetProperty("id", out _));
        Assert.True(first.TryGetProperty("applier_type", out var applierType));
        Assert.Equal("user", applierType.GetString());
        Assert.True(first.TryGetProperty("applier", out _));
        Assert.True(first.TryGetProperty("opportunity", out _));
        Assert.True(first.TryGetProperty("status", out var status));
        Assert.Equal("pending", status.GetString());
        Assert.True(first.TryGetProperty("created_at", out _));
    }

    [Fact]
    public async Task Show_OwnApplication_Returns200()
    {
        var opp = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId, name: "Detail target");
        var appId = await OaoHelpers.SeedApplicationAsync(_factory, opp, applicantUserId: OaoHelpers.Worker.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync($"/api/users/opportunities/applications/{appId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal(appId, doc.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Show_OtherUsersApplication_Returns404()
    {
        var opp = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId, name: "Other detail");
        var otherAppId = await OaoHelpers.SeedApplicationAsync(_factory, opp, applicantUserId: OaoHelpers.Worker2.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync($"/api/users/opportunities/applications/{otherAppId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LegacyAndCanonical_ReturnSamePayload()
    {
        var opp = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId, name: "Dual");
        await OaoHelpers.SeedApplicationAsync(_factory, opp, applicantUserId: OaoHelpers.Worker.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var legacy = await client.GetStringAsync("/api/users/opportunities/applications");
        var canonical = await client.GetStringAsync("/api/v1/users/opportunities/applications");
        Assert.Equal(legacy, canonical);
    }
}
