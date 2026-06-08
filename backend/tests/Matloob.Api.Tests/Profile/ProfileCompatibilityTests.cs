using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Common;
using Matloob.Api.Tests.Establishments;

namespace Matloob.Api.Tests.Profile;

/// <summary>
/// Tests for the Phase-C profile compatibility endpoints:
/// <c>GET /api/v1/profile</c> + <c>GET /api/users/profile</c> (alias) and
/// <c>GET /api/users/profile/establishment-list</c> +
/// <c>GET /api/v1/users/profile/establishment-list</c> (alias).
///
/// Response shape mirrors Laravel snake_case keys (UserResource for
/// /profile, EstablishmentResource for /establishment-list).
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
    public async Task Profile_AuthenticatedUser_ReturnsLaravelShape()
    {
        var client = _factory.CreateClientFor(FreshUser);

        var response = await client.GetAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        // Laravel UserResource is wrapped in a { data } envelope.
        var data = doc.RootElement.GetProperty("data");

        // Laravel UserResource snake_case keys.
        Assert.Equal(FreshUser.Sub,
            data.GetProperty("identity_id").GetString());
        Assert.Equal(JsonValueKind.Null,
            data.GetProperty("nationality").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            data.GetProperty("id_number").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            data.GetProperty("bank_account").ValueKind);
        Assert.Equal(JsonValueKind.Array,
            data.GetProperty("languages").ValueKind);
        Assert.Equal(JsonValueKind.Array,
            data.GetProperty("professions").ValueKind);
        Assert.Equal(JsonValueKind.Array,
            data.GetProperty("certificates").ValueKind);
        Assert.Equal(JsonValueKind.Array,
            data.GetProperty("uncompleted_profile_sections").ValueKind);
        Assert.False(data.GetProperty("onboarded").GetBoolean());
        Assert.Equal(0,
            data.GetProperty("profile_complete_percentage").GetInt32());
    }

    [Fact]
    public async Task Profile_BothRoutes_ReturnSameLogicalPayload()
    {
        var client = _factory.CreateClientFor(FreshUser);

        // Compare structural identity + key set; LastSeenAt is not in
        // the payload so we can do this safely.
        using var v1Doc = JsonDocument.Parse(await client.GetStringAsync("/api/v1/profile"));
        using var legacyDoc = JsonDocument.Parse(await client.GetStringAsync("/api/users/profile"));

        var v1Data = v1Doc.RootElement.GetProperty("data");
        var legacyData = legacyDoc.RootElement.GetProperty("data");

        Assert.Equal(
            v1Data.GetProperty("identity_id").GetString(),
            legacyData.GetProperty("identity_id").GetString());
        Assert.Equal(
            v1Data.GetProperty("id").GetGuid(),
            legacyData.GetProperty("id").GetGuid());

        var v1Props = v1Data.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();
        var legacyProps = legacyData.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();
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
        Assert.Equal(JsonValueKind.Array, doc.RootElement.DataOf().ValueKind);
        Assert.Equal(0, doc.RootElement.DataOf().GetArrayLength());
    }

    [Fact]
    public async Task EstablishmentList_ActiveMember_ReturnsLaravelShape()
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
        var mine = doc.RootElement.DataOf().EnumerateArray()
            .Single(e => e.GetProperty("id").GetGuid() == id);

        // Laravel snake_case shape.
        Assert.Equal("establishment", mine.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("logo").ValueKind);
        Assert.False(string.IsNullOrEmpty(mine.GetProperty("labor_office_id").GetString()));
        Assert.False(string.IsNullOrEmpty(mine.GetProperty("sequence_number").GetString()));
        // New-client extensions.
        Assert.Equal("Manager", mine.GetProperty("role").GetString());
        Assert.Equal("Approved", mine.GetProperty("status").GetString());

        // permissions[] matches the Manager role map (has events.manage, lacks
        // the Owner-only members.manage and the Owner-only profile.edit).
        var perms = mine.GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString()).ToList();
        Assert.Contains("events.manage", perms);
        Assert.Contains("offers.send", perms);
        Assert.DoesNotContain("members.manage", perms);
        Assert.DoesNotContain("profile.edit", perms);
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

        var listResp = await creator.GetAsync("/api/users/profile/establishment-list");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);

        await using var stream = await listResp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        foreach (var item in doc.RootElement.DataOf().EnumerateArray())
        {
            var status = item.GetProperty("status").GetString();
            Assert.NotEqual("Draft", status);
            Assert.NotEqual("Rejected", status);
            Assert.NotEqual("PendingReview", status);
        }
    }
}
