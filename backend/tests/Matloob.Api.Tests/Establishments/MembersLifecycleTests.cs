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
/// Phase 8B members CRUD tests. The narrative arc:
///  1. Approval creates the first Owner.
///  2. Owner adds HR.
///  3. HR can list members but cannot add anyone.
///  4. Owner updates HR -> Accountant.
///  5. Owner soft-deletes the Accountant.
///  6. Owner cannot demote / deactivate / remove themselves -- the
///     last-Owner protection rejects all three.
///
/// Plus the AssetAccessRules tests for establishment-member grants on
/// downloads.
/// </summary>
public sealed class MembersLifecycleTests : IClassFixture<EstablishmentsApiFactory>, IAsyncLifetime
{
    private readonly EstablishmentsApiFactory _factory;

    public MembersLifecycleTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    // -- shared setup --------------------------------------------------------

    private static readonly TestUser HR = new(
        Sub: "estab-hr-1",
        Roles: new[] { "matloob_user" });

    /// <summary>
    /// Production middleware (<c>CurrentUserSyncMiddleware</c>) provisions
    /// the local users row on a caller's first authenticated request.
    /// Tests AddMember(HR.Sub) before HR has made any call, so we seed
    /// the row directly. Idempotent; safe to re-run before every test.
    /// "third-user" appears in one negative test as a target third member.
    /// </summary>
    public async Task InitializeAsync()
    {
        await Helpers.SeedLocalUserAsync(_factory, HR.Sub);
        await Helpers.SeedLocalUserAsync(_factory, "third-user");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Full path: create Draft -> fill in -> link docs -> submit -> admin
    /// approves. Returns the establishment id; the creator becomes an
    /// active Owner as part of approval.
    /// </summary>
    private async Task<Guid> CreateApprovedEstablishmentAsync(string crNumber)
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var id = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: crNumber);

        var submit = await creator.PostAsync(
            $"/api/v1/establishments/registration/{id}/submit", content: null);
        submit.EnsureSuccessStatusCode();

        var approve = await admin.PostAsync(
            $"/api/v1/admin/establishments/{id}/approve", content: null);
        approve.EnsureSuccessStatusCode();

        return id;
    }

    // -- list ----------------------------------------------------------------

    [Fact]
    public async Task ListMembers_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-LIST-1");

        var response = await anon.GetAsync($"/api/v1/establishments/{id}/members");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListMembers_NonMember_ReturnsForbidden()
    {
        var other = _factory.CreateClientFor(Helpers.OtherUser);
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-LIST-2");

        var response = await other.GetAsync($"/api/v1/establishments/{id}/members");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListMembers_AsAdmin_Allowed()
    {
        var admin = _factory.CreateClientFor(Helpers.Admin);
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-LIST-3");

        var response = await admin.GetAsync($"/api/v1/establishments/{id}/members");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListMembers_ApprovalCreatesFirstOwner()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-OWNER-1");

        var response = await creator.GetAsync($"/api/v1/establishments/{id}/members");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var members = doc.RootElement.GetProperty("members").EnumerateArray().ToList();
        Assert.Single(members);
        Assert.Equal(Helpers.Creator.Sub, members[0].GetProperty("userId").GetString());
        Assert.Equal("Owner", members[0].GetProperty("role").GetString());
        Assert.True(members[0].GetProperty("isActive").GetBoolean());
    }

    // -- add -----------------------------------------------------------------

    [Fact]
    public async Task AddMember_OwnerCanAdd()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-ADD-1");

        var response = await creator.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "HR" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AddMember_NonOwner_ReturnsForbidden()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var hr = _factory.CreateClientFor(HR);
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-ADD-2");

        // Owner adds HR as a non-Owner member.
        var add = await creator.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "HR" });
        add.EnsureSuccessStatusCode();

        // HR tries to add another member -> 403 (not Owner).
        var third = await hr.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = "third-user", role = "Manager" });
        Assert.Equal(HttpStatusCode.Forbidden, third.StatusCode);
    }

    [Fact]
    public async Task AddMember_DuplicateActive_ReturnsConflict()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-ADD-3");

        await creator.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "HR" });

        var dup = await creator.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "Accountant" });

        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task AddMember_DraftEstablishment_ReturnsConflictForAdmin()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        // Create a draft, but DON'T submit/approve. The creator isn't yet an
        // Owner (the Owner row only exists after approval), so the creator
        // would get a 403 here. Use an admin call to bypass that and reach
        // the actual status guard.
        var draft = await creator.PostAsync(
            "/api/v1/establishments/registration/drafts", content: null);
        var id = await Helpers.ReadIdAsync(draft);

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "HR" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // -- update role ---------------------------------------------------------

    [Fact]
    public async Task UpdateMember_OwnerChangesRole()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-PATCH-1");

        // Add HR.
        var addResponse = await creator.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "HR" });
        var addBody = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        var memberId = addBody.GetProperty("id").GetGuid();

        // Update HR -> Accountant.
        var patch = await creator.PatchAsJsonAsync(
            $"/api/v1/establishments/{id}/members/{memberId}",
            new { role = "Accountant" });

        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.EstablishmentMembers.AsNoTracking().SingleAsync(m => m.Id == memberId);
        Assert.Equal(EstablishmentMemberRole.Accountant, row.Role);
    }

    [Fact]
    public async Task UpdateMember_LastOwnerCannotBeDemoted()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-OWN-1");

        // Find the creator's Owner row id.
        Guid ownerMemberId;
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            ownerMemberId = (await db.EstablishmentMembers.AsNoTracking()
                .SingleAsync(m => m.EstablishmentId == id && m.UserId == Helpers.Creator.Sub)).Id;
        }

        var patch = await creator.PatchAsJsonAsync(
            $"/api/v1/establishments/{id}/members/{ownerMemberId}",
            new { role = "Manager" });

        Assert.Equal(HttpStatusCode.Conflict, patch.StatusCode);
    }

    [Fact]
    public async Task UpdateMember_LastOwnerCannotBeDeactivated()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-OWN-2");

        Guid ownerMemberId;
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            ownerMemberId = (await db.EstablishmentMembers.AsNoTracking()
                .SingleAsync(m => m.EstablishmentId == id && m.UserId == Helpers.Creator.Sub)).Id;
        }

        var patch = await creator.PatchAsJsonAsync(
            $"/api/v1/establishments/{id}/members/{ownerMemberId}",
            new { isActive = false });

        Assert.Equal(HttpStatusCode.Conflict, patch.StatusCode);
    }

    [Fact]
    public async Task UpdateMember_AdminCanChangeRole()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-ADMIN-PATCH");

        var add = await creator.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "HR" });
        var memberId = (await add.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var patch = await admin.PatchAsJsonAsync(
            $"/api/v1/establishments/{id}/members/{memberId}",
            new { role = "Manager" });

        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
    }

    // -- remove --------------------------------------------------------------

    [Fact]
    public async Task RemoveMember_OwnerCanRemoveMember()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-DEL-1");

        var add = await creator.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "HR" });
        var memberId = (await add.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var del = await creator.DeleteAsync($"/api/v1/establishments/{id}/members/{memberId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        // Removed member no longer appears in the list.
        var list = await creator.GetAsync($"/api/v1/establishments/{id}/members");
        var listBody = await list.Content.ReadFromJsonAsync<JsonElement>();
        var memberIds = listBody.GetProperty("members").EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid()).ToList();
        Assert.DoesNotContain(memberId, memberIds);
    }

    [Fact]
    public async Task RemoveMember_LastOwner_ReturnsConflict()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-DEL-OWN");

        Guid ownerMemberId;
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            ownerMemberId = (await db.EstablishmentMembers.AsNoTracking()
                .SingleAsync(m => m.EstablishmentId == id && m.UserId == Helpers.Creator.Sub)).Id;
        }

        var del = await creator.DeleteAsync($"/api/v1/establishments/{id}/members/{ownerMemberId}");
        Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_AdminCanRemove()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-DEL-ADMIN");

        var add = await creator.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "HR" });
        var memberId = (await add.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var del = await admin.DeleteAsync($"/api/v1/establishments/{id}/members/{memberId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_NonOwner_ReturnsForbidden()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var hr = _factory.CreateClientFor(HR);
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-DEL-NO");

        var add = await creator.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "HR" });
        var memberId = (await add.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        // HR tries to remove themselves -> 403 (not Owner).
        var del = await hr.DeleteAsync($"/api/v1/establishments/{id}/members/{memberId}");
        Assert.Equal(HttpStatusCode.Forbidden, del.StatusCode);
    }

    // -- AssetAccessRules establishment-member grant -------------------------

    [Fact]
    public async Task EstablishmentMember_CanDownloadEstablishmentAsset()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-ASSET-1");

        // Owner adds HR.
        await creator.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = HR.Sub, role = "HR" });

        // Seed a private asset belonging to the establishment (different
        // OwnerUserId so the static "owner" branch can't grant access).
        var assetId = Guid.NewGuid();
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Assets.Add(new Asset(
                id: assetId,
                originalFileName: "establishment-shared.pdf",
                storedFileName: $"{assetId:N}.pdf",
                contentType: "application/pdf",
                sizeBytes: 100,
                sha256: new string('b', 64),
                relativePath: $"test/{assetId:N}.pdf",
                storageDriver: AssetStorageDriver.Local,
                visibility: AssetVisibility.Private,
                purpose: AssetPurpose.Generic,
                ownerUserId: "someone-else",
                ownerEstablishmentId: id));
            await db.SaveChangesAsync();
        }

        var hrClient = _factory.CreateClientFor(HR);

        // HR is an active member of the establishment -> metadata returns 200
        // even though they don't own the asset.
        var metaResponse = await hrClient.GetAsync($"/api/v1/assets/{assetId}/metadata");
        Assert.Equal(HttpStatusCode.OK, metaResponse.StatusCode);
    }

    [Fact]
    public async Task NonMember_CannotDownloadEstablishmentAsset()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-MEM-ASSET-2");

        var assetId = Guid.NewGuid();
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Assets.Add(new Asset(
                id: assetId,
                originalFileName: "private.pdf",
                storedFileName: $"{assetId:N}.pdf",
                contentType: "application/pdf",
                sizeBytes: 100,
                sha256: new string('c', 64),
                relativePath: $"test/{assetId:N}.pdf",
                storageDriver: AssetStorageDriver.Local,
                visibility: AssetVisibility.Private,
                purpose: AssetPurpose.Generic,
                ownerUserId: "someone-else",
                ownerEstablishmentId: id));
            await db.SaveChangesAsync();
        }

        var other = _factory.CreateClientFor(Helpers.OtherUser);
        var meta = await other.GetAsync($"/api/v1/assets/{assetId}/metadata");

        Assert.Equal(HttpStatusCode.Forbidden, meta.StatusCode);
    }
}
