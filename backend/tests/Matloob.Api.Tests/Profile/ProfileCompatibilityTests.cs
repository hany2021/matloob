using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Establishments;

namespace Matloob.Api.Tests.Profile;

/// <summary>
/// Tests for the Phase-C profile compatibility endpoints:
/// <c>GET /api/v1/profile</c> + <c>GET /api/users/profile</c> (alias) and
/// <c>GET /api/users/profile/establishment-list</c> +
/// <c>GET /api/v1/users/profile/establishment-list</c> (alias).
///
/// Reuses EstablishmentsApiFactory because the establishment-list endpoint
/// needs joinable establishment + member rows.
/// </summary>
public sealed class ProfileCompatibilityTests
    : IClassFixture<EstablishmentsApiFactory>, IAsyncLifetime
{
    private readonly EstablishmentsApiFactory _factory;

    private static readonly TestUser FreshUser = new(
        Sub: "profile-fresh-user",
        Roles: new[] { "matloob_user" });

    private static readonly TestUser HR = new(
        Sub: "profile-hr-1",
        Roles: new[] { "matloob_user" });

    public ProfileCompatibilityTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync() => Helpers.SeedLocalUserAsync(_factory, HR.Sub);

    public Task DisposeAsync() => Task.CompletedTask;

    // -- /profile -----------------------------------------------------------

    [Fact]
    public async Task Profile_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var response = await anon.GetAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Profile_LegacyAlias_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var response = await anon.GetAsync("/api/users/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Profile_AuthenticatedUser_ReturnsCurrentRow()
    {
        var client = _factory.CreateClientFor(FreshUser);

        var response = await client.GetAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal(FreshUser.Sub,
            doc.RootElement.GetProperty("identityId").GetString());
        // Placeholder relations are present and well-typed (empty arrays /
        // explicit null) so the Laravel frontend's parser doesn't choke.
        Assert.Equal(JsonValueKind.Array,
            doc.RootElement.GetProperty("languages").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            doc.RootElement.GetProperty("nationality").ValueKind);
    }

    [Fact]
    public async Task Profile_BothRoutes_ReturnSamePayload()
    {
        var client = _factory.CreateClientFor(FreshUser);

        // The sync middleware refreshes LastSeenAt on every authenticated
        // request, so the two payloads won't be byte-identical. Compare the
        // identity fields and the relation-shape keys instead.
        using var v1Doc = JsonDocument.Parse(await client.GetStringAsync("/api/v1/profile"));
        using var legacyDoc = JsonDocument.Parse(await client.GetStringAsync("/api/users/profile"));

        Assert.Equal(
            v1Doc.RootElement.GetProperty("identityId").GetString(),
            legacyDoc.RootElement.GetProperty("identityId").GetString());
        Assert.Equal(
            v1Doc.RootElement.GetProperty("id").GetGuid(),
            legacyDoc.RootElement.GetProperty("id").GetGuid());

        var v1Props = v1Doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();
        var legacyProps = legacyDoc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();
        Assert.Equal(v1Props, legacyProps);
    }

    // -- /users/profile/establishment-list ----------------------------------

    [Fact]
    public async Task EstablishmentList_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var response = await anon.GetAsync("/api/users/profile/establishment-list");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EstablishmentList_NoMemberships_ReturnsEmptyArray()
    {
        var client = _factory.CreateClientFor(FreshUser);

        var response = await client.GetAsync("/api/users/profile/establishment-list");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task EstablishmentList_ActiveMember_ReturnsTheirEstablishment()
    {
        // Build an Approved establishment and add HR as a member.
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var id = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: "CR-PROF-LIST-1");
        await creator.PostAsync($"/api/v1/establishments/registration/{id}/submit", content: null);
        await admin.PostAsync($"/api/v1/admin/establishments/{id}/approve", content: null);

        var addResp = await creator.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "Manager" });
        Assert.Equal(HttpStatusCode.Created, addResp.StatusCode);

        var hr = _factory.CreateClientFor(HR);
        var listResp = await hr.GetAsync("/api/users/profile/establishment-list");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);

        await using var stream = await listResp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var ids = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToList();
        Assert.Contains(id, ids);

        var mine = doc.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("id").GetGuid() == id);
        Assert.Equal("establishment", mine.GetProperty("type").GetString());
        Assert.Equal("Manager", mine.GetProperty("role").GetString());
        Assert.Equal("Approved", mine.GetProperty("status").GetString());
    }

    [Fact]
    public async Task EstablishmentList_DraftEstablishment_NotReturned()
    {
        // Creator has a Draft they own but no Approved memberships -- the
        // list shows nothing (the matrix limits results to Approved /
        // Suspended).
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var draftResp = await creator.PostAsync(
            "/api/v1/establishments/registration/drafts", content: null);
        Assert.Equal(HttpStatusCode.Created, draftResp.StatusCode);

        // Use a brand-new client whose sub is the creator's sub but who has
        // no memberships beyond their own (Draft).
        var listResp = await creator.GetAsync("/api/users/profile/establishment-list");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);

        await using var stream = await listResp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        // The creator might have Approved establishments from earlier
        // tests in this class; assert only that no DRAFT shows up.
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var status = item.GetProperty("status").GetString();
            Assert.NotEqual("Draft", status);
            Assert.NotEqual("Rejected", status);
            Assert.NotEqual("PendingReview", status);
        }
    }
}
