using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Domain.Assets;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Establishments;

/// <summary>
/// Phase 8D — admin suspend / reinstate + 423 Locked behavior on writes.
///
/// Each test builds a fresh Approved establishment with the existing
/// helper, suspends it, then verifies:
/// - reads still work,
/// - every mutation endpoint returns 423,
/// - in-flight ChangeRequests are not auto-cancelled,
/// - history rows are appended for Suspended / Reinstated,
/// - reinstate flips back to Approved and writes unblock.
/// </summary>
public sealed class SuspensionLifecycleTests : IClassFixture<EstablishmentsApiFactory>, IAsyncLifetime
{
    private static readonly TestUser HR = new(
        Sub: "estab-hr-suspense",
        Roles: new[] { "matloob_user" });

    private readonly EstablishmentsApiFactory _factory;

    public SuspensionLifecycleTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Seed the local users row for HR; AddMember enforces presence
    /// (spec §6.3) and HR never makes their own request before being
    /// added.
    /// </summary>
    public Task InitializeAsync() => Helpers.SeedLocalUserAsync(_factory, HR.Sub);

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> CreateApprovedEstablishmentAsync(string crNumber)
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var id = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: crNumber);
        await creator.PostAsync($"/api/v1/establishments/registration/{id}/submit", content: null);
        await admin.PostAsync($"/api/v1/admin/establishments/{id}/approve", content: null);
        return id;
    }

    private async Task SuspendAsync(Guid id, string reason)
    {
        var admin = _factory.CreateClientFor(Helpers.Admin);
        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/establishments/{id}/suspend",
            new { reason });
        response.EnsureSuccessStatusCode();
    }

    // -- suspend -------------------------------------------------------------

    [Fact]
    public async Task Suspend_Anonymous_ReturnsUnauthorized()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-SUSP-1");
        var anon = _factory.CreateClientFor(null);

        var response = await anon.PostAsJsonAsync(
            $"/api/v1/admin/establishments/{id}/suspend", new { reason = "x" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Suspend_NonAdmin_ReturnsForbidden()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-SUSP-2");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        var response = await owner.PostAsJsonAsync(
            $"/api/v1/admin/establishments/{id}/suspend", new { reason = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Suspend_ApprovedEstablishment_StampsFieldsAndAppendsHistory()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-SUSP-3");
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/establishments/{id}/suspend",
            new { reason = "License revoked." });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await db.Establishments.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal(EstablishmentStatus.Suspended, row.Status);
        Assert.NotNull(row.SuspendedAt);
        Assert.Equal(Helpers.Admin.Sub, row.SuspendedByAdminId);
        Assert.Equal("License revoked.", row.SuspensionReason);

        var history = await db.EstablishmentReviewHistory.AsNoTracking()
            .Where(h => h.EstablishmentId == id)
            .Select(h => h.Action)
            .ToListAsync();
        Assert.Contains(EstablishmentReviewAction.Suspended, history);
    }

    [Fact]
    public async Task Suspend_NonApprovedEstablishment_ReturnsConflict()
    {
        // PendingReview -- submit but don't approve.
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var id = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: "CR-SUSP-PEND");
        await creator.PostAsync($"/api/v1/establishments/registration/{id}/submit", content: null);

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/establishments/{id}/suspend",
            new { reason = "Trying too early." });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Suspend_AlreadySuspended_ReturnsConflict()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-SUSP-DBL");
        await SuspendAsync(id, "first");

        var admin = _factory.CreateClientFor(Helpers.Admin);
        var second = await admin.PostAsJsonAsync(
            $"/api/v1/admin/establishments/{id}/suspend",
            new { reason = "second" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Suspend_RequiresReason()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-SUSP-EMPTY");
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/establishments/{id}/suspend",
            new { reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -- reinstate -----------------------------------------------------------

    [Fact]
    public async Task Reinstate_RequiresAdmin()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-REIN-1");
        await SuspendAsync(id, "test");

        var anon = _factory.CreateClientFor(null);
        var owner = _factory.CreateClientFor(Helpers.Creator);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PostAsync($"/api/v1/admin/establishments/{id}/reinstate", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.PostAsync($"/api/v1/admin/establishments/{id}/reinstate", content: null)).StatusCode);
    }

    [Fact]
    public async Task Reinstate_FlipsToApprovedAndClearsSuspensionFields()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-REIN-2");
        await SuspendAsync(id, "to be reversed");
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var response = await admin.PostAsync(
            $"/api/v1/admin/establishments/{id}/reinstate", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Establishments.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal(EstablishmentStatus.Approved, row.Status);
        Assert.Null(row.SuspendedAt);
        Assert.Null(row.SuspendedByAdminId);
        Assert.Null(row.SuspensionReason);

        // History rows: BOTH Suspended and Reinstated are present.
        var history = await db.EstablishmentReviewHistory.AsNoTracking()
            .Where(h => h.EstablishmentId == id)
            .Select(h => h.Action)
            .ToListAsync();
        Assert.Contains(EstablishmentReviewAction.Suspended, history);
        Assert.Contains(EstablishmentReviewAction.Reinstated, history);
    }

    [Fact]
    public async Task Reinstate_NonSuspended_ReturnsConflict()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-REIN-3");
        var admin = _factory.CreateClientFor(Helpers.Admin);

        // Establishment is still Approved -- reinstate should refuse.
        var response = await admin.PostAsync(
            $"/api/v1/admin/establishments/{id}/reinstate", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // -- reads allowed while suspended ---------------------------------------

    [Fact]
    public async Task Suspended_ReadsStillWork()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-SUSP-READ");
        await SuspendAsync(id, "freeze");

        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        // Owner-side member list still returns 200.
        var members = await creator.GetAsync($"/api/v1/establishments/{id}/members");
        Assert.Equal(HttpStatusCode.OK, members.StatusCode);

        // Admin review + history still return 200.
        var review = await admin.GetAsync($"/api/v1/admin/establishments/{id}/review");
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);

        var history = await admin.GetAsync($"/api/v1/admin/establishments/{id}/review-history");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
    }

    // -- writes blocked while suspended (423) --------------------------------

    [Fact]
    public async Task Suspended_AddMember_Returns423()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-SUSP-WRITE-1");
        await SuspendAsync(id, "freeze");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        var response = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "HR" });

        Assert.Equal(HttpStatusCode.Locked, response.StatusCode);
        await AssertErrorCodeAsync(response, "establishment_suspended");
    }

    [Fact]
    public async Task Suspended_UpdateMember_Returns423()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-SUSP-WRITE-2");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        // Add HR while still Approved.
        var add = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "HR" });
        var memberId = (await add.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await SuspendAsync(id, "freeze");

        var patch = await owner.PatchAsJsonAsync(
            $"/api/v1/establishments/{id}/members/{memberId}",
            new { role = "Manager" });

        Assert.Equal(HttpStatusCode.Locked, patch.StatusCode);
        await AssertErrorCodeAsync(patch, "establishment_suspended");
    }

    [Fact]
    public async Task Suspended_RemoveMember_Returns423()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-SUSP-WRITE-3");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        var add = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "HR" });
        var memberId = (await add.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await SuspendAsync(id, "freeze");

        var del = await owner.DeleteAsync($"/api/v1/establishments/{id}/members/{memberId}");
        Assert.Equal(HttpStatusCode.Locked, del.StatusCode);
        await AssertErrorCodeAsync(del, "establishment_suspended");
    }

    [Fact]
    public async Task Suspended_CreateChangeRequest_Returns423()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-SUSP-WRITE-4");
        await SuspendAsync(id, "freeze");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        var response = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);

        Assert.Equal(HttpStatusCode.Locked, response.StatusCode);
        await AssertErrorCodeAsync(response, "establishment_suspended");
    }

    [Fact]
    public async Task Suspended_ExistingChangeRequest_CannotBeEditedOrSubmitted()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-SUSP-WRITE-5");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        // Open a CR while still Approved.
        var crResponse = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = (await crResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await SuspendAsync(id, "freeze mid-edit");

        var patch = await owner.PatchAsJsonAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/basic-info",
            new { name = "Should NOT apply" });
        Assert.Equal(HttpStatusCode.Locked, patch.StatusCode);

        var submit = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/submit", content: null);
        Assert.Equal(HttpStatusCode.Locked, submit.StatusCode);

        // The CR row itself is still Draft -- not auto-cancelled.
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cr = await db.EstablishmentChangeRequests.AsNoTracking().SingleAsync(c => c.Id == crId);
        Assert.Equal(EstablishmentChangeRequestStatus.Draft, cr.Status);
    }

    [Fact]
    public async Task Suspended_AttachProposedDocument_Returns423()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-SUSP-WRITE-6");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        var crResponse = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = (await crResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        // Seed an AuthorizationLetter asset before suspending.
        var newAssetId = Guid.NewGuid();
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Assets.Add(Helpers.MakeAsset(newAssetId, AssetPurpose.AuthorizationLetter, Helpers.Creator.Sub));
            await db.SaveChangesAsync();
        }

        await SuspendAsync(id, "freeze before attach");

        var attach = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/documents/authorization-letter",
            new { assetId = newAssetId });

        Assert.Equal(HttpStatusCode.Locked, attach.StatusCode);
        await AssertErrorCodeAsync(attach, "establishment_suspended");
    }

    [Fact]
    public async Task Suspended_AttachProposedCommercialRegistration_Returns423()
    {
        // Mirror of Suspended_AttachProposedDocument_Returns423 but for the
        // CommercialRegistration route. Both endpoints share the same
        // handler (AttachProposedDocumentHandler) so the suspended branch
        // is identical -- this test makes that explicit so future changes
        // to either route can't regress one without the other being noticed.
        var id = await CreateApprovedEstablishmentAsync("CR-SUSP-WRITE-CR");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        var crResponse = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = (await crResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var newAssetId = Guid.NewGuid();
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Assets.Add(Helpers.MakeAsset(newAssetId, AssetPurpose.CommercialRegistration, Helpers.Creator.Sub));
            await db.SaveChangesAsync();
        }

        await SuspendAsync(id, "freeze before CR attach");

        var attach = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/documents/commercial-registration",
            new { assetId = newAssetId });

        Assert.Equal(HttpStatusCode.Locked, attach.StatusCode);
        await AssertErrorCodeAsync(attach, "establishment_suspended");
    }

    // -- reinstate restores write capability ---------------------------------

    [Fact]
    public async Task Reinstate_AllowsWritesAgain()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-REIN-WRITE");
        await SuspendAsync(id, "temp");
        var admin = _factory.CreateClientFor(Helpers.Admin);
        await admin.PostAsync($"/api/v1/admin/establishments/{id}/reinstate", content: null);

        var owner = _factory.CreateClientFor(Helpers.Creator);
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "HR" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // -- helpers -------------------------------------------------------------

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal(expectedCode, doc.RootElement.GetProperty("code").GetString());
    }
}
