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
/// Integration tests covering the Phase 8A onboarding lifecycle: update
/// basic info → upload documents → submit → admin review → approve / reject
/// → resubmit. Each fact uses a freshly-created draft so cross-test state
/// is irrelevant.
/// </summary>
public sealed class OnboardingLifecycleTests : IClassFixture<EstablishmentsApiFactory>
{
    private readonly EstablishmentsApiFactory _factory;

    public OnboardingLifecycleTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    // -- update basic info ---------------------------------------------------

    [Fact]
    public async Task UpdateBasicInfo_WithoutUser_ReturnsUnauthorized()
    {
        var client = _factory.CreateClientFor(null);

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/establishments/registration/{Guid.NewGuid()}/basic-info",
            new { name = "anything" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateBasicInfo_NonCreator_ReturnsForbidden()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var other = _factory.CreateClientFor(Helpers.OtherUser);

        var draft = await creator.PostAsync("/api/v1/establishments/registration/drafts", content: null);
        var id = await Helpers.ReadIdAsync(draft);

        var response = await other.PatchAsJsonAsync(
            $"/api/v1/establishments/registration/{id}/basic-info",
            new { name = "hijack" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateBasicInfo_ByCreator_StoresFields()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);

        var draft = await creator.PostAsync("/api/v1/establishments/registration/drafts", content: null);
        var id = await Helpers.ReadIdAsync(draft);

        var response = await creator.PatchAsJsonAsync(
            $"/api/v1/establishments/registration/{id}/basic-info",
            new
            {
                name = "Acme",
                commercialRegistrationNumber = "CR-100",
                email = "ops@acme.test",
                phone = "+966500000000",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Establishments.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal("Acme", row.Name);
        Assert.Equal("CR-100", row.CommercialRegistrationNumber);
        Assert.Equal("ops@acme.test", row.Email);
        Assert.Equal("+966500000000", row.Phone);
    }

    // -- document upload -----------------------------------------------------

    [Fact]
    public async Task LinkDocument_LinksAssetToEstablishment()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);

        var draft = await creator.PostAsync("/api/v1/establishments/registration/drafts", content: null);
        var id = await Helpers.ReadIdAsync(draft);

        var assetId = Guid.NewGuid();
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Assets.Add(Helpers.MakeAsset(assetId, AssetPurpose.AuthorizationLetter, Helpers.Creator.Sub));
            await db.SaveChangesAsync();
        }

        var response = await creator.PostAsJsonAsync(
            $"/api/v1/establishments/registration/{id}/documents/authorization-letter",
            new { assetId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var doc = await db.EstablishmentDocuments
                .AsNoTracking()
                .SingleAsync(d => d.EstablishmentId == id
                    && d.DocumentType == EstablishmentDocumentType.AuthorizationLetter);
            Assert.Equal(assetId, doc.AssetId);
        }
    }

    [Fact]
    public async Task LinkDocument_WrongPurpose_ReturnsConflict()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);

        var draft = await creator.PostAsync("/api/v1/establishments/registration/drafts", content: null);
        var id = await Helpers.ReadIdAsync(draft);

        // Asset has Purpose=CommercialRegistration but we link as AuthorizationLetter.
        var assetId = Guid.NewGuid();
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Assets.Add(Helpers.MakeAsset(assetId, AssetPurpose.CommercialRegistration, Helpers.Creator.Sub));
            await db.SaveChangesAsync();
        }

        var response = await creator.PostAsJsonAsync(
            $"/api/v1/establishments/registration/{id}/documents/authorization-letter",
            new { assetId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // -- submit --------------------------------------------------------------

    [Fact]
    public async Task Submit_MissingRequiredFields_ReturnsBadRequest()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);

        var draft = await creator.PostAsync("/api/v1/establishments/registration/drafts", content: null);
        var id = await Helpers.ReadIdAsync(draft);

        // No basic-info patch, no documents.
        var response = await creator.PostAsync(
            $"/api/v1/establishments/registration/{id}/submit", content: null);

        // Missing documents trip first; the endpoint returns 400.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Submit_MissingDocuments_ReturnsBadRequest()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);

        var draft = await creator.PostAsync("/api/v1/establishments/registration/drafts", content: null);
        var id = await Helpers.ReadIdAsync(draft);

        // Fill required scalar fields but don't link any documents.
        await creator.PatchAsJsonAsync(
            $"/api/v1/establishments/registration/{id}/basic-info",
            new
            {
                name = "Acme",
                commercialRegistrationNumber = "CR-MISSING-DOCS",
                laborOfficeId = "1",
                sequenceNumber = "1",
                city = "Riyadh",
                email = "ops@acme.test",
                phone = "+966500000000",
            });

        var response = await creator.PostAsync(
            $"/api/v1/establishments/registration/{id}/submit", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Submit_CompleteDraft_TransitionsToPendingReview()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: "CR-SUBMIT-OK-1");

        var response = await creator.PostAsync(
            $"/api/v1/establishments/registration/{id}/submit", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Establishments.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal(EstablishmentStatus.PendingReview, row.Status);
        Assert.NotNull(row.SubmittedAt);

        // A Submitted history row was appended.
        var history = await db.EstablishmentReviewHistory.AsNoTracking()
            .Where(h => h.EstablishmentId == id)
            .ToListAsync();
        Assert.Contains(history, h => h.Action == EstablishmentReviewAction.Submitted);
    }

    [Fact]
    public async Task Submit_DuplicateCrNumber_ReturnsConflict()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var other = _factory.CreateClientFor(Helpers.OtherUser);

        // First establishment claims CR-DUPE and reaches PendingReview.
        var firstId = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: "CR-DUPE");
        var first = await creator.PostAsync(
            $"/api/v1/establishments/registration/{firstId}/submit", content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second establishment, different user, same CR -> 409 at submit time.
        var secondId = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, other, commercialRegistrationNumber: "CR-DUPE",
            creatorSub: Helpers.OtherUser.Sub);
        var second = await other.PostAsync(
            $"/api/v1/establishments/registration/{secondId}/submit", content: null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // -- admin queries -------------------------------------------------------

    [Fact]
    public async Task AdminPendingReview_RequiresAdmin()
    {
        var nonAdmin = _factory.CreateClientFor(Helpers.Creator);
        var anon = _factory.CreateClientFor(null);

        var responseNonAdmin = await nonAdmin.GetAsync("/api/v1/admin/establishments/pending-review");
        var responseAnon = await anon.GetAsync("/api/v1/admin/establishments/pending-review");

        Assert.Equal(HttpStatusCode.Forbidden, responseNonAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, responseAnon.StatusCode);
    }

    [Fact]
    public async Task AdminPendingReview_AsAdmin_ReturnsSubmittedRows()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var id = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: "CR-QUEUE-1");
        await creator.PostAsync($"/api/v1/establishments/registration/{id}/submit", content: null);

        var response = await admin.GetAsync("/api/v1/admin/establishments/pending-review");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var items = doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToList();
        Assert.Contains(id, items);
    }

    // -- approve / reject ----------------------------------------------------

    [Fact]
    public async Task Approve_CreatesOwnerMemberAndFlipsStatus()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var id = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: "CR-APPROVE-1");
        await creator.PostAsync($"/api/v1/establishments/registration/{id}/submit", content: null);

        var response = await admin.PostAsync(
            $"/api/v1/admin/establishments/{id}/approve", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await db.Establishments.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal(EstablishmentStatus.Approved, row.Status);
        Assert.NotNull(row.ApprovedAt);
        Assert.Equal(Helpers.Admin.Sub, row.ApprovedByAdminId);

        var owner = await db.EstablishmentMembers.AsNoTracking()
            .SingleAsync(m =>
                m.EstablishmentId == id &&
                m.UserId == Helpers.Creator.Sub &&
                m.Role == EstablishmentMemberRole.Owner);
        Assert.True(owner.IsActive);
    }

    [Fact]
    public async Task Reject_StoresReasonAndAllowsResubmit()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var id = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: "CR-REJECT-1");
        await creator.PostAsync($"/api/v1/establishments/registration/{id}/submit", content: null);

        var reject = await admin.PostAsJsonAsync(
            $"/api/v1/admin/establishments/{id}/reject",
            new { reason = "Trade license missing." });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Establishments.AsNoTracking().SingleAsync(e => e.Id == id);
            Assert.Equal(EstablishmentStatus.Rejected, row.Status);
            Assert.Equal("Trade license missing.", row.RejectionReason);
        }

        // Creator can now edit the rejected row.
        var patch = await creator.PatchAsJsonAsync(
            $"/api/v1/establishments/registration/{id}/basic-info",
            new { name = "Acme Events Co (corrected)" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        // And resubmit.
        var resubmit = await creator.PostAsync(
            $"/api/v1/establishments/registration/{id}/submit", content: null);
        Assert.Equal(HttpStatusCode.OK, resubmit.StatusCode);

        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Establishments.AsNoTracking().SingleAsync(e => e.Id == id);
            Assert.Equal(EstablishmentStatus.PendingReview, row.Status);
            // SubmitForReview clears the Rejected* triplet on the live row;
            // history still has the Rejected event.
            Assert.Null(row.RejectionReason);
            var history = await db.EstablishmentReviewHistory.AsNoTracking()
                .Where(h => h.EstablishmentId == id)
                .Select(h => h.Action)
                .ToListAsync();
            Assert.Contains(EstablishmentReviewAction.Rejected, history);
            Assert.Equal(2, history.Count(a => a == EstablishmentReviewAction.Submitted));
        }
    }
}
