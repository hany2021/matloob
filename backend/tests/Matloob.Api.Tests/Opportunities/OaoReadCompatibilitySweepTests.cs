using System.Net;
using System.Text.Json;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// Cross-cutting compatibility sweep for the Phase OAO-2 read endpoints.
///
/// <para>
/// Goal: a single fixture that walks every shipped read URL, asserts the
/// happy-path 200, the snake_case key shape, the absence of any
/// Ajeer / contract / invoice fields anywhere in the JSON tree, and the
/// parity between legacy and canonical URLs where both exist.
/// </para>
/// </summary>
public sealed class OaoReadCompatibilitySweepTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private static readonly string[] ForbiddenSubstrings =
    [
        "ajeer",
        "contract", // intentionally bans contract_path / contracts_count / contract object
        "invoice",
    ];

    private readonly OpportunitiesApiFactory _factory;
    private Guid _ownEstablishment;
    private Guid _publisherEstablishment;
    private Guid _workerApplicationId;
    private Guid _orgApplicationId;
    private Guid _ownOpportunity;
    private Guid _publisherOpportunity;

    private static readonly Auth.TestUser Publisher = new(
        Sub: "oao-sweep-publisher",
        Roles: new[] { "matloob_user" });

    public OaoReadCompatibilitySweepTests(OpportunitiesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, Publisher.Sub);

        _ownEstablishment = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-OAO-SWEEP-A");
        _publisherEstablishment = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, Publisher.Sub, "CR-OAO-SWEEP-B");

        // One vacancy opportunity on each side so user-browse and
        // establishment-browse both have data.
        _ownOpportunity = await OaoHelpers.SeedOpportunityAsync(
            _factory, _ownEstablishment, name: "Sweep own", forVacancy: true);
        _publisherOpportunity = await OaoHelpers.SeedOpportunityAsync(
            _factory, _publisherEstablishment, name: "Sweep pub", forVacancy: false);

        _workerApplicationId = await OaoHelpers.SeedApplicationAsync(
            _factory, _ownOpportunity, applicantUserId: OaoHelpers.Worker.Sub);
        _orgApplicationId = await OaoHelpers.SeedApplicationAsync(
            _factory, _publisherOpportunity,
            applicantEstablishmentId: _ownEstablishment,
            appliedByUserId: OaoHelpers.EstablishmentOwner.Sub);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public static IEnumerable<object[]> AllUserReadEndpoints() => new[]
    {
        new object[] { "user", "/api/users/opportunities" },
        new object[] { "user", "/api/v1/users/opportunities" },
        new object[] { "user", "/api/users/opportunities/applications" },
        new object[] { "user", "/api/v1/users/opportunities/applications" },
    };

    [Theory]
    [MemberData(nameof(AllUserReadEndpoints))]
    public async Task UserReadEndpoint_ReturnsOk_NoForbiddenFields(string _, string url)
    {
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        AssertNoForbiddenJsonFields(json, url);
    }

    [Fact]
    public async Task UserBrowse_LegacyAndCanonical_ReturnSamePayload()
    {
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        Assert.Equal(
            await client.GetStringAsync("/api/users/opportunities"),
            await client.GetStringAsync("/api/v1/users/opportunities"));
    }

    [Fact]
    public async Task UserShow_LegacyAndCanonical_ReturnSamePayload()
    {
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        Assert.Equal(
            await client.GetStringAsync($"/api/users/opportunities/{_ownOpportunity}"),
            await client.GetStringAsync($"/api/v1/users/opportunities/{_ownOpportunity}"));
    }

    [Fact]
    public async Task EstablishmentBrowse_LegacyAndCanonical_ReturnSamePayload()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        Assert.Equal(
            await client.GetStringAsync(
                $"/api/establishments/opportunities?establishment_id={_ownEstablishment}"),
            await client.GetStringAsync(
                $"/api/v1/establishments/{_ownEstablishment}/browse/opportunities"));
    }

    [Fact]
    public async Task EstablishmentBrowseShow_LegacyAndCanonical_ReturnSamePayload()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        Assert.Equal(
            await client.GetStringAsync(
                $"/api/establishments/opportunities/{_publisherOpportunity}?establishment_id={_ownEstablishment}"),
            await client.GetStringAsync(
                $"/api/v1/establishments/{_ownEstablishment}/browse/opportunities/{_publisherOpportunity}"));
    }

    [Fact]
    public async Task OwnerList_LegacyAndCanonical_ReturnSamePayload()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        Assert.Equal(
            await client.GetStringAsync(
                $"/api/establishments/me/opportunities?establishment_id={_ownEstablishment}"),
            await client.GetStringAsync(
                $"/api/v1/establishments/{_ownEstablishment}/opportunities"));
    }

    [Fact]
    public async Task OwnerShow_LegacyAndCanonical_ReturnSamePayload()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        Assert.Equal(
            await client.GetStringAsync(
                $"/api/establishments/me/opportunities/{_ownOpportunity}?establishment_id={_ownEstablishment}"),
            await client.GetStringAsync(
                $"/api/v1/establishments/{_ownEstablishment}/opportunities/{_ownOpportunity}"));
    }

    [Fact]
    public async Task CategoriesEndpoint_NoForbiddenFields()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_ownEstablishment}/browse/opportunity-categories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        AssertNoForbiddenJsonFields(json, "opportunity-categories");
    }

    [Fact]
    public async Task EstablishmentBrowseApplications_LegacyAndCanonical_ReturnSamePayload()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        Assert.Equal(
            await client.GetStringAsync(
                $"/api/establishments/opportunities/applications?establishment_id={_ownEstablishment}"),
            await client.GetStringAsync(
                $"/api/v1/establishments/{_ownEstablishment}/browse/applications"));
    }

    [Fact]
    public async Task BrowseApplicationDetail_OrganizationApplierType()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.GetAsync(
            $"/api/v1/establishments/{_ownEstablishment}/browse/applications/{_orgApplicationId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("organization", doc.RootElement.GetProperty("applier_type").GetString());
    }

    [Fact]
    public async Task UserApplicationDetail_UserApplierType()
    {
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.GetAsync(
            $"/api/users/opportunities/applications/{_workerApplicationId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("user", doc.RootElement.GetProperty("applier_type").GetString());
    }

    // -- helper -------------------------------------------------------------

    private static void AssertNoForbiddenJsonFields(string json, string url)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var key in EnumerateKeys(doc.RootElement))
        {
            var lower = key.ToLowerInvariant();
            foreach (var bad in ForbiddenSubstrings)
            {
                Assert.False(lower.Contains(bad),
                    $"Endpoint {url} returned a forbidden key '{key}' (matches '{bad}'). " +
                    "Ajeer / contract / invoice fields must not appear in responses; " +
                    "see docs/25-ajeer-disposition.md.");
            }
        }
    }

    private static IEnumerable<string> EnumerateKeys(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    yield return prop.Name;
                    foreach (var nested in EnumerateKeys(prop.Value))
                    {
                        yield return nested;
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in EnumerateKeys(item))
                    {
                        yield return nested;
                    }
                }
                break;
        }
    }
}
