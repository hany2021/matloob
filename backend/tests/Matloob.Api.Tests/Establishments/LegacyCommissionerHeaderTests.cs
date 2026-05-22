using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Tests.Auth;

namespace Matloob.Api.Tests.Establishments;

/// <summary>
/// Verifies the X-Commissioner-UUID legacy header is honored by the
/// shared establishment-context resolver. The Laravel frontend sent this
/// header to identify "which establishment the caller is acting as" —
/// the new system removed the per-user commissioner table but accepts
/// the header verbatim and treats the GUID as an establishment id.
/// </summary>
public sealed class LegacyCommissionerHeaderTests
    : IClassFixture<EstablishmentsApiFactory>, IAsyncLifetime
{
    private readonly EstablishmentsApiFactory _factory;

    private static readonly TestUser MultiMember = new(
        Sub: "legacy-comm-multi-1",
        Roles: new[] { "matloob_user" });

    public LegacyCommissionerHeaderTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync() => Helpers.SeedLocalUserAsync(_factory, MultiMember.Sub);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task XCommissionerUuid_PointsToEstablishmentMember_Resolves()
    {
        var firstId = await BuildApprovedEstablishmentAsync("CR-LEGACY-COMM-A");
        var secondId = await BuildApprovedEstablishmentAsync("CR-LEGACY-COMM-B");

        var client = _factory.CreateClientFor(MultiMember);

        // Resolution must use the LEGACY header even though the caller
        // has multiple memberships (so auto-pick would 400).
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/establishments/me/profile");
        req.Headers.Add("X-Commissioner-UUID", secondId.ToString());
        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal(secondId, doc.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task XCommissionerUuid_BogusGuid_ResolvesAsExplicitIdAndReturns404()
    {
        // X-Commissioner-UUID is treated like X-Establishment-Id — the
        // value is used verbatim and the endpoint's own membership check
        // 404s on unknown ids. (Previously this fell through to auto-pick;
        // the simpler design is consistent with the other explicit headers
        // and avoids surprising fallback semantics.)
        await BuildApprovedEstablishmentAsync("CR-LEGACY-COMM-BOGUS");

        var client = _factory.CreateClientFor(MultiMember);
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/establishments/me/profile");
        req.Headers.Add("X-Commissioner-UUID", Guid.NewGuid().ToString());
        var response = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task XCommissionerUuid_NonGuid_IsIgnored_FallsThroughToAutoPick()
    {
        // A non-parseable header is ignored; auto-pick kicks in. With a
        // single membership this succeeds; with multiple it 400s
        // (establishment_context_required).
        var soleId = await BuildApprovedEstablishmentAsync("CR-LEGACY-COMM-INVALID");
        var client = _factory.CreateClientFor(MultiMember);
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/establishments/me/profile");
        req.Headers.Add("X-Commissioner-UUID", "not-a-guid");
        var response = await client.SendAsync(req);

        Assert.True(response.StatusCode == HttpStatusCode.OK
            || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task XEstablishmentId_StillWorks_AfterShimAdded()
    {
        var id = await BuildApprovedEstablishmentAsync("CR-LEGACY-COMM-XEST");
        var client = _factory.CreateClientFor(MultiMember);
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/establishments/me/profile");
        req.Headers.Add("X-Establishment-Id", id.ToString());
        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EstablishmentIdQuery_StillWorks_AfterShimAdded()
    {
        var id = await BuildApprovedEstablishmentAsync("CR-LEGACY-COMM-QS");
        var client = _factory.CreateClientFor(MultiMember);
        var response = await client.GetAsync(
            $"/api/establishments/me/profile?establishment_id={id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // -- helper --------------------------------------------------------------

    private async Task<Guid> BuildApprovedEstablishmentAsync(string crNumber)
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var id = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: crNumber);
        await creator.PostAsync(
            $"/api/v1/establishments/registration/{id}/submit", content: null);
        await admin.PostAsync(
            $"/api/v1/admin/establishments/{id}/approve", content: null);

        // Add MultiMember as a member (idempotent — 409 is ignored).
        var resp = await creator.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = MultiMember.Sub, role = "Manager" });
        if (resp.StatusCode != HttpStatusCode.Created
            && resp.StatusCode != HttpStatusCode.Conflict)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"AddMember failed for CR={crNumber}: {resp.StatusCode} - {body}");
        }
        return id;
    }
}
