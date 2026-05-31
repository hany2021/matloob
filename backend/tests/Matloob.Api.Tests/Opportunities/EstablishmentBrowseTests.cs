using System.Net;
using System.Text.Json;
using Matloob.Api.Tests.Common;
using Matloob.Domain.Opportunities;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// Tests for the establishment-browse opportunity endpoints
/// (<c>GET /api/establishments/opportunities</c>,
/// <c>/{id}</c>, and <c>/categories</c>) plus the canonical
/// <c>/api/v1/establishments/{establishmentId}/browse/...</c> aliases.
/// </summary>
public sealed class EstablishmentBrowseTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;

    // Establishment with one active member (EstablishmentOwner). Used as
    // the applying-establishment in browse.
    private Guid _applyingEstablishmentId;

    // A second establishment, owned by a different sub, that publishes
    // opportunities the applying establishment can browse.
    private Guid _publishingEstablishmentId;

    private static readonly Auth.TestUser PublishingOwner = new(
        Sub: "oao-publisher-1",
        Roles: new[] { "matloob_user" });

    public EstablishmentBrowseTests(OpportunitiesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Outsider.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, PublishingOwner.Sub);

        _applyingEstablishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-OAO-EB-A");
        _publishingEstablishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, PublishingOwner.Sub, "CR-OAO-EB-B");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -- list browse --------------------------------------------------------

    [Fact]
    public async Task List_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var response = await anon.GetAsync("/api/establishments/opportunities");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_OutsiderWithNoMembership_Returns404()
    {
        var outsider = _factory.CreateClientFor(OaoHelpers.Outsider);
        var response = await outsider.GetAsync("/api/establishments/opportunities");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_CanonicalRoute_ActiveMember_ReturnsNonVacancyOpportunities()
    {
        await OaoHelpers.SeedOpportunityAsync(_factory, _publishingEstablishmentId,
            name: "Catering opp", forVacancy: false);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_applyingEstablishmentId}/browse/opportunities");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Contains(doc.RootElement.DataOf().EnumerateArray(),
            e => e.GetProperty("name").GetString() == "Catering opp");
    }

    [Fact]
    public async Task List_LegacyRoute_ResolvesByQueryParam()
    {
        await OaoHelpers.SeedOpportunityAsync(_factory, _publishingEstablishmentId,
            name: "QS resolved", forVacancy: false);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/establishments/opportunities?establishment_id={_applyingEstablishmentId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Contains(doc.RootElement.DataOf().EnumerateArray(),
            e => e.GetProperty("name").GetString() == "QS resolved");
    }

    [Fact]
    public async Task List_ExcludesSelfCreatedOpportunities()
    {
        // The applying establishment publishes one of its own — this MUST
        // NOT appear on the browse list.
        await OaoHelpers.SeedOpportunityAsync(_factory, _applyingEstablishmentId,
            name: "Self-created", forVacancy: false);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_applyingEstablishmentId}/browse/opportunities");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        Assert.DoesNotContain(doc.RootElement.DataOf().EnumerateArray(),
            e => e.GetProperty("name").GetString() == "Self-created");
    }

    [Fact]
    public async Task List_ExcludesVacancyCategoryOpportunities()
    {
        await OaoHelpers.SeedOpportunityAsync(_factory, _publishingEstablishmentId,
            name: "Worker job", forVacancy: true);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_applyingEstablishmentId}/browse/opportunities");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        Assert.DoesNotContain(doc.RootElement.DataOf().EnumerateArray(),
            e => e.GetProperty("name").GetString() == "Worker job");
    }

    // -- show --------------------------------------------------------------

    [Fact]
    public async Task Show_UnknownId_Returns404()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_applyingEstablishmentId}/browse/opportunities/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_ReturnsLaravelShape()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _publishingEstablishmentId,
            name: "Detail target", forVacancy: false);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_applyingEstablishmentId}/browse/opportunities/{oppId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var data = doc.RootElement.DataOf();
        Assert.Equal(oppId, data.GetProperty("id").GetGuid());
        Assert.True(data.TryGetProperty("opportunity_category", out _));
        Assert.False(data.TryGetProperty("contracts_count", out _));
    }

    [Fact]
    public async Task Show_OwnOpportunity_Returns404()
    {
        var ownOpp = await OaoHelpers.SeedOpportunityAsync(_factory, _applyingEstablishmentId,
            name: "Own one", forVacancy: false);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_applyingEstablishmentId}/browse/opportunities/{ownOpp}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -- categories --------------------------------------------------------

    [Fact]
    public async Task Categories_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var response = await anon.GetAsync("/api/establishments/opportunities/categories");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Categories_ActiveMember_ReturnsTopLevelCategoriesWithChildren()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_applyingEstablishmentId}/browse/opportunity-categories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var data = doc.RootElement.DataOf();
        Assert.True(data.GetArrayLength() > 0);

        var first = data.EnumerateArray().First();
        Assert.True(first.TryGetProperty("id", out _));
        Assert.True(first.TryGetProperty("title", out _));
        Assert.True(first.TryGetProperty("for_vacancy", out _));
        Assert.True(first.TryGetProperty("is_other", out _));
        Assert.True(first.TryGetProperty("children", out var children));
        Assert.Equal(JsonValueKind.Array, children.ValueKind);

        // No top-level "Other" rows.
        Assert.DoesNotContain(data.EnumerateArray(),
            e => e.GetProperty("is_other").GetBoolean());
    }
}
