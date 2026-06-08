using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Common;

namespace Matloob.Api.Tests.Establishments;

/// <summary>
/// Compatibility tests for <c>GET /api/establishments/me/profile</c> +
/// <c>GET /api/v1/establishments/me/profile</c>. Verifies establishment
/// resolution (query / header / auto-resolve) and the Laravel composite
/// response shape.
/// </summary>
public sealed class MeProfileCompatibilityTests
    : IClassFixture<EstablishmentsApiFactory>, IAsyncLifetime
{
    private readonly EstablishmentsApiFactory _factory;

    // Tests share a fixture, so each test that needs "exactly one
    // membership" semantics uses its own user sub to avoid cross-test
    // contamination (a sub joined to multiple establishments would trip
    // the auto-resolve 400 path).
    private static readonly TestUser AutoUser = new(
        Sub: "me-prof-auto-1",
        Roles: new[] { "matloob_user" });

    private static readonly TestUser QueryUser = new(
        Sub: "me-prof-query-1",
        Roles: new[] { "matloob_user" });

    private static readonly TestUser HeaderUser = new(
        Sub: "me-prof-header-1",
        Roles: new[] { "matloob_user" });

    private static readonly TestUser DualUser = new(
        Sub: "me-prof-dual-1",
        Roles: new[] { "matloob_user" });

    private static readonly TestUser MultiMember = new(
        Sub: "me-prof-multi-1",
        Roles: new[] { "matloob_user" });

    private static readonly TestUser Stranger = new(
        Sub: "me-prof-stranger-1",
        Roles: new[] { "matloob_user" });

    // Owner of the "other" establishment in the cross-user 404 test —
    // kept separate so they don't accidentally pick up a second
    // membership and break the auto-resolve test.
    private static readonly TestUser OtherOwner = new(
        Sub: "me-prof-other-owner-1",
        Roles: new[] { "matloob_user" });

    public MeProfileCompatibilityTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await Helpers.SeedLocalUserAsync(_factory, AutoUser.Sub);
        await Helpers.SeedLocalUserAsync(_factory, QueryUser.Sub);
        await Helpers.SeedLocalUserAsync(_factory, HeaderUser.Sub);
        await Helpers.SeedLocalUserAsync(_factory, DualUser.Sub);
        await Helpers.SeedLocalUserAsync(_factory, MultiMember.Sub);
        await Helpers.SeedLocalUserAsync(_factory, Stranger.Sub);
        await Helpers.SeedLocalUserAsync(_factory, OtherOwner.Sub);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task MeProfile_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var response = await anon.GetAsync("/api/establishments/me/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MeProfile_NoMembership_ReturnsNotFound()
    {
        var stranger = _factory.CreateClientFor(Stranger);
        var response = await stranger.GetAsync("/api/establishments/me/profile");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MeProfile_SoleMembership_AutoResolves_AndReturnsLaravelShape()
    {
        var id = await BuildApprovedEstablishmentWithMember("CR-ME-PROF-1", AutoUser.Sub);

        var client = _factory.CreateClientFor(AutoUser);
        var response = await client.GetAsync("/api/establishments/me/profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var data = doc.RootElement.DataOf();

        // Laravel EstablishmentResource top-level keys.
        Assert.Equal(id, data.GetProperty("id").GetGuid());
        Assert.False(string.IsNullOrEmpty(data.GetProperty("name").GetString()));
        Assert.False(string.IsNullOrEmpty(data.GetProperty("email").GetString()));
        // Top-level lifecycle status (the public frontend gates Approved-only
        // screens like employee management on this exact field).
        Assert.Equal("Approved", data.GetProperty("status").GetString());
        // 5 sections × 20% (legacy EstablishmentSupport formula). This freshly
        // approved establishment only has general-info populated (city) — no
        // services/products, additional contact number, bank account, or
        // years-of-experience — so exactly one section counts.
        Assert.Equal(20, data.GetProperty("profile_complete_percentage").GetInt32());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("logo").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("rate").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("total_reviews").ValueKind);
        Assert.False(data.GetProperty("can_manage_events").GetBoolean());

        // New-client extensions: the caller's role + permission slugs. The
        // member was added as Manager (see BuildApprovedEstablishmentWithMember).
        Assert.Equal("Manager", data.GetProperty("active_role").GetString());
        var activePerms = data.GetProperty("active_permissions").EnumerateArray()
            .Select(p => p.GetString()).ToList();
        Assert.Contains("events.manage", activePerms);
        Assert.DoesNotContain("members.manage", activePerms);
        Assert.DoesNotContain("profile.edit", activePerms);

        // Profile composite block.
        var profile = data.GetProperty("profile");
        var general = profile.GetProperty("general_info");
        Assert.Equal("Approved", general.GetProperty("establishment_status").GetString());
        Assert.Equal("CR-ME-PROF-1", general.GetProperty("cr_number").GetString());
        Assert.Equal("Riyadh", general.GetProperty("city").GetString());

        var contact = profile.GetProperty("contact_info");
        Assert.Equal("+966500000000", contact.GetProperty("contact_number").GetString());
        Assert.Equal("ops@acme.test", contact.GetProperty("email").GetString());

        // Placeholders for not-yet-migrated relations.
        Assert.Equal(JsonValueKind.Array, profile.GetProperty("services").ValueKind);
        Assert.Equal(JsonValueKind.Array, profile.GetProperty("products").ValueKind);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("bank_account").ValueKind);
        Assert.Equal(JsonValueKind.Array, profile.GetProperty("experiences").ValueKind);
    }

    [Fact]
    public async Task MeProfile_QueryParam_ResolvesExplicitEstablishment()
    {
        var id = await BuildApprovedEstablishmentWithMember("CR-ME-PROF-Q", QueryUser.Sub);
        var client = _factory.CreateClientFor(QueryUser);

        var response = await client.GetAsync($"/api/establishments/me/profile?establishment_id={id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal(id, doc.RootElement.DataOf().GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task MeProfile_Header_ResolvesExplicitEstablishment()
    {
        var id = await BuildApprovedEstablishmentWithMember("CR-ME-PROF-H", HeaderUser.Sub);
        var client = _factory.CreateClientFor(HeaderUser);

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/establishments/me/profile");
        req.Headers.Add("X-Establishment-Id", id.ToString());
        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal(id, doc.RootElement.DataOf().GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task MeProfile_MultipleMemberships_NoContext_Returns400()
    {
        await BuildApprovedEstablishmentWithMember("CR-ME-MULTI-A", MultiMember.Sub);
        await BuildApprovedEstablishmentWithMember("CR-ME-MULTI-B", MultiMember.Sub);

        var client = _factory.CreateClientFor(MultiMember);
        var response = await client.GetAsync("/api/establishments/me/profile");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("establishment_context_required",
            doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MeProfile_LegacyAndCanonical_ReturnSamePayload()
    {
        var id = await BuildApprovedEstablishmentWithMember("CR-ME-DUAL", DualUser.Sub);
        var client = _factory.CreateClientFor(DualUser);

        var legacy = await client.GetStringAsync($"/api/establishments/me/profile?establishment_id={id}");
        var canonical = await client.GetStringAsync($"/api/v1/establishments/me/profile?establishment_id={id}");

        Assert.Equal(legacy, canonical);
    }

    [Fact]
    public async Task MeProfile_OtherUsersEstablishmentById_Returns404()
    {
        var otherId = await BuildApprovedEstablishmentWithMember("CR-ME-OTHER", OtherOwner.Sub);

        var stranger = _factory.CreateClientFor(Stranger);
        var response = await stranger.GetAsync(
            $"/api/establishments/me/profile?establishment_id={otherId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -- helpers --------------------------------------------------------------

    private async Task<Guid> BuildApprovedEstablishmentWithMember(string crNumber, string memberSub)
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var id = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: crNumber);
        await creator.PostAsync($"/api/v1/establishments/registration/{id}/submit", content: null);
        await admin.PostAsync($"/api/v1/admin/establishments/{id}/approve", content: null);

        // If the member is the creator themselves, they're already implicitly
        // in the inner circle but not yet a Member row. Add them explicitly
        // so the membership-based resolver finds them.
        var addResp = await creator.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = memberSub, role = "Manager" });
        // Idempotent: already-a-member returns 409, which is fine for tests.
        if (addResp.StatusCode != HttpStatusCode.Created
            && addResp.StatusCode != HttpStatusCode.Conflict)
        {
            addResp.EnsureSuccessStatusCode();
        }
        return id;
    }
}
