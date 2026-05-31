using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Common;
using Matloob.Domain.Assets;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Establishments;

/// <summary>
/// Phase 8C lifecycle tests for EstablishmentChangeRequest. Each test
/// builds a fresh Approved establishment via the same helper used by
/// Phase 8B and then drives the ChangeRequest endpoints.
/// </summary>
public sealed class ChangeRequestLifecycleTests : IClassFixture<EstablishmentsApiFactory>
{
    private readonly EstablishmentsApiFactory _factory;

    public ChangeRequestLifecycleTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    // -- shared setup --------------------------------------------------------

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

    private async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.DataOf().GetProperty("id").GetGuid();
    }

    // -- create --------------------------------------------------------------

    [Fact]
    public async Task CreateChangeRequest_Anonymous_ReturnsUnauthorized()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-CR-CREATE-1");
        var anon = _factory.CreateClientFor(null);

        var response = await anon.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateChangeRequest_NonOwner_ReturnsForbidden()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-CR-CREATE-2");
        var other = _factory.CreateClientFor(Helpers.OtherUser);

        var response = await other.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateChangeRequest_OwnerCanCreate()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-CR-CREATE-3");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        var response = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var crId = await ReadIdAsync(response);
        Assert.NotEqual(Guid.Empty, crId);
    }

    [Fact]
    public async Task CreateChangeRequest_DraftEstablishment_ReturnsConflict()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        // Bare draft - establishment Status = Draft.
        var draft = await creator.PostAsync(
            "/api/v1/establishments/registration/drafts", content: null);
        var id = await Helpers.ReadIdAsync(draft);

        // Admin call -- bypass the Owner check and exercise the status guard.
        var response = await admin.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateChangeRequest_SecondInFlight_ReturnsConflict()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-CR-CREATE-4");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        var first = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // -- update proposed basic-info ------------------------------------------

    [Fact]
    public async Task UpdateProposedBasicInfo_OwnerCanUpdate_LiveStaysUnchanged()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-CR-PATCH-1");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        var crResponse = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await ReadIdAsync(crResponse);

        var patch = await owner.PatchAsJsonAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/basic-info",
            new { name = "Acme Renamed" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cr = await db.EstablishmentChangeRequests.AsNoTracking().SingleAsync(c => c.Id == crId);
        Assert.Equal("Acme Renamed", cr.ProposedName);

        // Live establishment row is unchanged.
        var est = await db.Establishments.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal("Acme Events Co", est.Name);
    }

    [Fact]
    public async Task UpdateProposedBasicInfo_NonOwner_ReturnsForbidden()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-CR-PATCH-2");
        var owner = _factory.CreateClientFor(Helpers.Creator);
        var other = _factory.CreateClientFor(Helpers.OtherUser);

        var crResponse = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await ReadIdAsync(crResponse);

        var patch = await other.PatchAsJsonAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/basic-info",
            new { name = "hijack" });
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
    }

    [Fact]
    public async Task UpdateProposedBasicInfo_InvalidEmail_ReturnsUnprocessableEntity()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-CR-PATCH-3");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        var crResponse = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await ReadIdAsync(crResponse);

        var patch = await owner.PatchAsJsonAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/basic-info",
            new { email = "not-an-email" });

        // FluentValidation failures now return Laravel-style 422.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, patch.StatusCode);
    }

    // -- proposed document attach --------------------------------------------

    [Fact]
    public async Task AttachProposedDocument_OwnerCanAttach_LiveDocsUnchanged()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-CR-DOC-1");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        var crResponse = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await ReadIdAsync(crResponse);

        // Seed a fresh AuthorizationLetter asset owned by the creator.
        var newAssetId = Guid.NewGuid();
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Assets.Add(Helpers.MakeAsset(newAssetId, AssetPurpose.AuthorizationLetter, Helpers.Creator.Sub));
            await db.SaveChangesAsync();
        }

        var attach = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/documents/authorization-letter",
            new { assetId = newAssetId });
        Assert.Equal(HttpStatusCode.Created, attach.StatusCode);

        // CR carries the proposed id but the live EstablishmentDocument row
        // still references the ORIGINAL onboarding asset.
        using var scope2 = _factory.CreateDbScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var cr = await db2.EstablishmentChangeRequests.AsNoTracking().SingleAsync(c => c.Id == crId);
        Assert.Equal(newAssetId, cr.ProposedAuthorizationLetterAssetId);

        var liveDoc = await db2.EstablishmentDocuments.AsNoTracking()
            .SingleAsync(d => d.EstablishmentId == id
                && d.DocumentType == EstablishmentDocumentType.AuthorizationLetter);
        Assert.NotEqual(newAssetId, liveDoc.AssetId);
    }

    [Fact]
    public async Task AttachProposedDocument_WrongPurpose_ReturnsConflict()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-CR-DOC-2");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        var crResponse = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await ReadIdAsync(crResponse);

        var mismatchAssetId = Guid.NewGuid();
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Assets.Add(Helpers.MakeAsset(mismatchAssetId, AssetPurpose.CommercialRegistration, Helpers.Creator.Sub));
            await db.SaveChangesAsync();
        }

        var attach = await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/documents/authorization-letter",
            new { assetId = mismatchAssetId });
        Assert.Equal(HttpStatusCode.Conflict, attach.StatusCode);
    }

    // -- submit --------------------------------------------------------------

    [Fact]
    public async Task SubmitChangeRequest_EmptyDraft_ReturnsBadRequest()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-CR-SUB-1");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        var crResponse = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await ReadIdAsync(crResponse);

        var submit = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/submit", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, submit.StatusCode);
    }

    [Fact]
    public async Task SubmitChangeRequest_WithChanges_TransitionsToPendingReview()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-CR-SUB-2");
        var owner = _factory.CreateClientFor(Helpers.Creator);

        var crResponse = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await ReadIdAsync(crResponse);

        await owner.PatchAsJsonAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/basic-info",
            new { phone = "+966500000099" });

        var submit = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/submit", content: null);
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cr = await db.EstablishmentChangeRequests.AsNoTracking().SingleAsync(c => c.Id == crId);
        Assert.Equal(EstablishmentChangeRequestStatus.PendingReview, cr.Status);
        Assert.NotNull(cr.SubmittedAt);

        // History row appended.
        var history = await db.EstablishmentReviewHistory.AsNoTracking()
            .Where(h => h.EstablishmentId == id)
            .Select(h => h.Action)
            .ToListAsync();
        Assert.Contains(EstablishmentReviewAction.ChangeRequestSubmitted, history);
    }

    [Fact]
    public async Task SubmitChangeRequest_DuplicateProposedCrNumber_ReturnsConflict()
    {
        // Establishment A keeps a separate CR; establishment B opens a CR
        // proposing A's CR number -> 409.
        var idA = await CreateApprovedEstablishmentAsync("CR-CR-DUP-A");
        var idB = await CreateApprovedEstablishmentAsync("CR-CR-DUP-B");

        var owner = _factory.CreateClientFor(Helpers.Creator);

        var crResponse = await owner.PostAsync(
            $"/api/v1/establishments/{idB}/change-requests", content: null);
        var crId = await ReadIdAsync(crResponse);

        await owner.PatchAsJsonAsync(
            $"/api/v1/establishments/{idB}/change-requests/{crId}/basic-info",
            new { commercialRegistrationNumber = "CR-CR-DUP-A" });

        var submit = await owner.PostAsync(
            $"/api/v1/establishments/{idB}/change-requests/{crId}/submit", content: null);
        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);
    }

    // -- admin queries -------------------------------------------------------

    [Fact]
    public async Task AdminPendingChangeRequests_RequiresAdmin()
    {
        var owner = _factory.CreateClientFor(Helpers.Creator);
        var anon = _factory.CreateClientFor(null);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.GetAsync("/api/v1/admin/establishments/change-requests/pending")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync("/api/v1/admin/establishments/change-requests/pending")).StatusCode);
    }

    [Fact]
    public async Task AdminDetail_ContainsLiveAndProposed()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-CR-DETAIL-1");
        var owner = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var crResponse = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await ReadIdAsync(crResponse);

        await owner.PatchAsJsonAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/basic-info",
            new { name = "Acme Renamed" });
        await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/submit", content: null);

        var detail = await admin.GetAsync($"/api/v1/admin/establishments/change-requests/{crId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var body = await detail.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.DataOf();
        Assert.Equal("Acme Events Co", data.GetProperty("live").GetProperty("name").GetString());
        Assert.Equal("Acme Renamed", data.GetProperty("proposed").GetProperty("name").GetString());
    }

    // -- approve / reject ----------------------------------------------------

    [Fact]
    public async Task ApproveChangeRequest_AppliesProposedFields()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-CR-APP-1");
        var owner = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var crResponse = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await ReadIdAsync(crResponse);

        await owner.PatchAsJsonAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/basic-info",
            new { name = "Acme Renamed", phone = "+966500009999" });
        await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/submit", content: null);

        var approve = await admin.PostAsync(
            $"/api/v1/admin/establishments/change-requests/{crId}/approve", content: null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var est = await db.Establishments.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal("Acme Renamed", est.Name);
        Assert.Equal("+966500009999", est.Phone);
        Assert.Equal(EstablishmentStatus.Approved, est.Status); // unchanged.

        var cr = await db.EstablishmentChangeRequests.AsNoTracking().SingleAsync(c => c.Id == crId);
        Assert.Equal(EstablishmentChangeRequestStatus.Approved, cr.Status);
        Assert.NotNull(cr.AppliedAt);
    }

    [Fact]
    public async Task ApproveChangeRequest_ReplacesProposedDocuments()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-CR-APP-2");
        var owner = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        // Capture the original live AuthorizationLetter id.
        Guid originalLetterAssetId;
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            originalLetterAssetId = (await db.EstablishmentDocuments.AsNoTracking()
                .SingleAsync(d => d.EstablishmentId == id
                    && d.DocumentType == EstablishmentDocumentType.AuthorizationLetter)).AssetId;
        }

        var crResponse = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await ReadIdAsync(crResponse);

        // Seed a new AuthorizationLetter asset and attach it as proposed.
        var newAssetId = Guid.NewGuid();
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Assets.Add(Helpers.MakeAsset(newAssetId, AssetPurpose.AuthorizationLetter, Helpers.Creator.Sub));
            await db.SaveChangesAsync();
        }
        await owner.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/documents/authorization-letter",
            new { assetId = newAssetId });
        await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/submit", content: null);

        var approve = await admin.PostAsync(
            $"/api/v1/admin/establishments/change-requests/{crId}/approve", content: null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        using var scope2 = _factory.CreateDbScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

        // The live AuthorizationLetter slot now references the new asset.
        var liveDoc = await db2.EstablishmentDocuments.AsNoTracking()
            .SingleAsync(d => d.EstablishmentId == id
                && d.DocumentType == EstablishmentDocumentType.AuthorizationLetter);
        Assert.Equal(newAssetId, liveDoc.AssetId);

        // The original Asset row is soft-deleted (hidden by the global filter).
        var originalStillVisible = await db2.Assets.AsNoTracking()
            .AnyAsync(a => a.Id == originalLetterAssetId);
        Assert.False(originalStillVisible);
    }

    [Fact]
    public async Task RejectChangeRequest_LiveUnchanged()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-CR-REJ-1");
        var owner = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var crResponse = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await ReadIdAsync(crResponse);

        await owner.PatchAsJsonAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/basic-info",
            new { name = "Should NOT Apply" });
        await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/submit", content: null);

        var reject = await admin.PostAsJsonAsync(
            $"/api/v1/admin/establishments/change-requests/{crId}/reject",
            new { reason = "Looks suspicious." });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var est = await db.Establishments.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal("Acme Events Co", est.Name);

        var cr = await db.EstablishmentChangeRequests.AsNoTracking().SingleAsync(c => c.Id == crId);
        Assert.Equal(EstablishmentChangeRequestStatus.Rejected, cr.Status);
        Assert.Equal("Looks suspicious.", cr.ReviewReason);
    }

    [Fact]
    public async Task ApproveChangeRequest_WrongStatus_ReturnsConflict()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-CR-WRONG-1");
        var owner = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        // Draft CR (not yet submitted) cannot be approved.
        var crResponse = await owner.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await ReadIdAsync(crResponse);

        var approve = await admin.PostAsync(
            $"/api/v1/admin/establishments/change-requests/{crId}/approve", content: null);
        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
    }
}
