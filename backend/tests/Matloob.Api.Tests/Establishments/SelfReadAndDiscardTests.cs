using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Establishments;

/// <summary>
/// Phase 8E integration tests for the user-side self-read endpoints
/// (list + details), draft discard, and ChangeRequest cancel.
/// </summary>
public sealed class SelfReadAndDiscardTests : IClassFixture<EstablishmentsApiFactory>
{
    private static readonly TestUser HR = new(
        Sub: "estab-hr-selfread",
        Roles: new[] { "matloob_user" });

    private readonly EstablishmentsApiFactory _factory;

    public SelfReadAndDiscardTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<Guid> CreateDraftAsync(HttpClient client)
    {
        var response = await client.PostAsync(
            "/api/v1/establishments/registration/drafts", content: null);
        response.EnsureSuccessStatusCode();
        return await Helpers.ReadIdAsync(response);
    }

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

    private static async Task<List<Guid>> ReadIdsAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToList();
    }

    // -- list mine -----------------------------------------------------------

    [Fact]
    public async Task ListMine_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);

        var response = await anon.GetAsync("/api/v1/establishments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListMine_CreatorSeesTheirDraft()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateDraftAsync(creator);

        var response = await creator.GetAsync("/api/v1/establishments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ids = await ReadIdsAsync(response);
        Assert.Contains(id, ids);
    }

    [Fact]
    public async Task ListMine_CreatorDoesNotSeeOtherUsersDraft()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var other = _factory.CreateClientFor(Helpers.OtherUser);

        var id = await CreateDraftAsync(creator);

        var response = await other.GetAsync("/api/v1/establishments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ids = await ReadIdsAsync(response);
        Assert.DoesNotContain(id, ids);
    }

    [Fact]
    public async Task ListMine_ActiveMemberSeesApprovedEstablishment()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-LIST-MEM");

        // Add HR as a member.
        await creator.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "HR" });

        var hr = _factory.CreateClientFor(HR);
        var response = await hr.GetAsync("/api/v1/establishments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ids = await ReadIdsAsync(response);
        Assert.Contains(id, ids);
    }

    [Fact]
    public async Task ListMine_OwnerSeesPermissionFlags()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-LIST-FLAGS");

        var response = await creator.GetAsync("/api/v1/establishments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var mine = doc.RootElement.GetProperty("items").EnumerateArray()
            .Single(e => e.GetProperty("id").GetGuid() == id);
        Assert.Equal("Owner", mine.GetProperty("myRole").GetString());
        Assert.True(mine.GetProperty("canManageMembers").GetBoolean());
        Assert.True(mine.GetProperty("canCreateChangeRequest").GetBoolean());
        // canEdit / canSubmit are false on Approved -- creator edits go through CR.
        Assert.False(mine.GetProperty("canEdit").GetBoolean());
        Assert.False(mine.GetProperty("canSubmit").GetBoolean());
    }

    // -- details -------------------------------------------------------------

    [Fact]
    public async Task Details_CreatorReadsDraft()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateDraftAsync(creator);

        var response = await creator.GetAsync($"/api/v1/establishments/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal(id, doc.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Draft", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Details_MemberReadsApproved_DocumentsIncluded()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-DETAIL-DOCS");

        await creator.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "HR" });

        var hr = _factory.CreateClientFor(HR);
        var response = await hr.GetAsync($"/api/v1/establishments/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var docs = doc.RootElement.GetProperty("documents").EnumerateArray()
            .Select(d => d.GetProperty("documentType").GetString())
            .ToList();
        Assert.Contains("AuthorizationLetter", docs);
        Assert.Contains("CommercialRegistration", docs);

        // HR is a member, so the members list should be present and include them.
        var memberIds = doc.RootElement.GetProperty("members").EnumerateArray()
            .Select(m => m.GetProperty("userId").GetString())
            .ToList();
        Assert.Contains(HR.Sub, memberIds);
    }

    [Fact]
    public async Task Details_NonRelatedUser_ReturnsNotFound()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var other = _factory.CreateClientFor(Helpers.OtherUser);
        var id = await CreateDraftAsync(creator);

        // 404, not 403 -- avoid id-enumeration leak.
        var response = await other.GetAsync($"/api/v1/establishments/{id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Details_AdminCanRead()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);
        var id = await CreateDraftAsync(creator);

        var response = await admin.GetAsync($"/api/v1/establishments/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Details_PendingChangeRequestSummarySurfaces()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-DETAIL-PEND");

        var crResponse = await creator.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await Helpers.ReadIdAsync(crResponse);

        var detail = await creator.GetAsync($"/api/v1/establishments/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        await using var stream = await detail.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var pending = doc.RootElement.GetProperty("pendingChangeRequest");
        Assert.NotEqual(JsonValueKind.Null, pending.ValueKind);
        Assert.Equal(crId, pending.GetProperty("id").GetGuid());
    }

    // -- draft discard -------------------------------------------------------

    [Fact]
    public async Task DiscardDraft_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);

        var response = await anon.DeleteAsync(
            $"/api/v1/establishments/registration/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DiscardDraft_NonCreator_ReturnsForbidden()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var other = _factory.CreateClientFor(Helpers.OtherUser);
        var id = await CreateDraftAsync(creator);

        var response = await other.DeleteAsync(
            $"/api/v1/establishments/registration/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DiscardDraft_CreatorCanDiscard_RowDisappearsFromList()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateDraftAsync(creator);

        var del = await creator.DeleteAsync(
            $"/api/v1/establishments/registration/{id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var list = await creator.GetAsync("/api/v1/establishments");
        var ids = await ReadIdsAsync(list);
        Assert.DoesNotContain(id, ids);
    }

    [Fact]
    public async Task DiscardDraft_PendingReviewEstablishment_ReturnsConflict()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);

        var id = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: "CR-DISCARD-PEND");
        await creator.PostAsync($"/api/v1/establishments/registration/{id}/submit", content: null);

        var del = await creator.DeleteAsync(
            $"/api/v1/establishments/registration/{id}");

        Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);
    }

    [Fact]
    public async Task DiscardDraft_ApprovedEstablishment_ReturnsConflict()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-DISCARD-APP");

        var del = await creator.DeleteAsync(
            $"/api/v1/establishments/registration/{id}");

        Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);
    }

    // -- change request cancel -----------------------------------------------

    [Fact]
    public async Task CancelChangeRequest_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);

        var response = await anon.DeleteAsync(
            $"/api/v1/establishments/{Guid.NewGuid()}/change-requests/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CancelChangeRequest_UnrelatedUser_ReturnsForbidden()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var other = _factory.CreateClientFor(Helpers.OtherUser);
        var id = await CreateApprovedEstablishmentAsync("CR-CANC-OTHER");

        var crResponse = await creator.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await Helpers.ReadIdAsync(crResponse);

        var cancel = await other.DeleteAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}");

        Assert.Equal(HttpStatusCode.Forbidden, cancel.StatusCode);
    }

    [Fact]
    public async Task CancelChangeRequest_SubmitterCanCancelDraft()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-CANC-DRAFT");

        var crResponse = await creator.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await Helpers.ReadIdAsync(crResponse);

        var cancel = await creator.DeleteAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}");
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cr = await db.EstablishmentChangeRequests.AsNoTracking().SingleAsync(c => c.Id == crId);
        Assert.Equal(EstablishmentChangeRequestStatus.Cancelled, cr.Status);
    }

    [Fact]
    public async Task CancelChangeRequest_UnblocksANewChangeRequest()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-CANC-UNBLOCK");

        var crResponse = await creator.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await Helpers.ReadIdAsync(crResponse);

        // Cancel the in-flight CR.
        var cancel = await creator.DeleteAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}");
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        // A new one can now open.
        var second = await creator.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task CancelChangeRequest_PendingReview_CanBeCancelledByOwner()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-CANC-PEND");

        var crResponse = await creator.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await Helpers.ReadIdAsync(crResponse);

        await creator.PatchAsJsonAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/basic-info",
            new { name = "Doesn't matter" });
        await creator.PostAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/submit", content: null);

        var cancel = await creator.DeleteAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}");
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
    }

    [Fact]
    public async Task CancelChangeRequest_AlreadyApproved_ReturnsConflict()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);
        var id = await CreateApprovedEstablishmentAsync("CR-CANC-APPROVED");

        var crResponse = await creator.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await Helpers.ReadIdAsync(crResponse);

        await creator.PatchAsJsonAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/basic-info",
            new { name = "Acme Tweaked" });
        await creator.PostAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}/submit", content: null);
        await admin.PostAsync(
            $"/api/v1/admin/establishments/change-requests/{crId}/approve", content: null);

        var cancel = await creator.DeleteAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}");
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
    }

    [Fact]
    public async Task CancelChangeRequest_AllowedWhileEstablishmentSuspended()
    {
        // Spec / decision: cancel reduces pending work without mutating the
        // live row, so the 423 lock does NOT apply.
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);
        var id = await CreateApprovedEstablishmentAsync("CR-CANC-SUSP");

        var crResponse = await creator.PostAsync(
            $"/api/v1/establishments/{id}/change-requests", content: null);
        var crId = await Helpers.ReadIdAsync(crResponse);

        await admin.PostAsJsonAsync(
            $"/api/v1/admin/establishments/{id}/suspend",
            new { reason = "freeze" });

        var cancel = await creator.DeleteAsync(
            $"/api/v1/establishments/{id}/change-requests/{crId}");

        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
    }
}
