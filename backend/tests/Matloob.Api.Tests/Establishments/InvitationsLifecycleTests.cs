using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Common;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Establishments;

/// <summary>
/// Branch 4 — invite-by-email management endpoints (Invite / List / Revoke /
/// Resend). All are Owner-only (<c>members.manage</c>) and require the
/// establishment to be Approved. Tests drive the canonical
/// <c>/api/v1/establishments/{id}/members/invitations</c> routes.
/// </summary>
public sealed class InvitationsLifecycleTests
    : IClassFixture<EstablishmentsApiFactory>
{
    private readonly EstablishmentsApiFactory _factory;

    public InvitationsLifecycleTests(EstablishmentsApiFactory factory) => _factory = factory;

    private async Task<Guid> CreateApprovedEstablishmentAsync(string crNumber)
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);
        var id = await Helpers.CreateReadyToSubmitDraftAsync(_factory, creator, crNumber);
        (await creator.PostAsync($"/api/v1/establishments/registration/{id}/submit", null))
            .EnsureSuccessStatusCode();
        (await admin.PostAsync($"/api/v1/admin/establishments/{id}/approve", null))
            .EnsureSuccessStatusCode();
        return id;
    }

    private static string InvitationsUrl(Guid id) =>
        $"/api/v1/establishments/{id}/members/invitations";

    // -- invite --------------------------------------------------------------

    [Fact]
    public async Task Invite_Owner_CreatesPendingWithoutLeakingToken()
    {
        var owner = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-INV-1");

        var response = await owner.PostAsJsonAsync(
            InvitationsUrl(id), new { email = "Manager@Test.com", role = "Manager" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JsonElement>()).DataOf();
        Assert.Equal("manager@test.com", body.GetProperty("email").GetString()); // normalized
        Assert.Equal("Manager", body.GetProperty("role").GetString());
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        Assert.False(body.TryGetProperty("token", out _), "raw token must never be in the body");
        Assert.False(body.TryGetProperty("tokenHash", out _), "token hash must not be exposed");

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.EstablishmentInvitations.AsNoTracking()
            .SingleAsync(i => i.EstablishmentId == id && i.Email == "manager@test.com");
        Assert.Equal(EstablishmentInvitationStatus.Pending, row.Status);
        Assert.Equal(64, row.TokenHash.Length);              // SHA-256 hex
        Assert.True(row.ExpiresAt > row.InvitedAt.AddDays(6)); // ~7 days out
        Assert.True(row.ExpiresAt < row.InvitedAt.AddDays(8));
    }

    [Fact]
    public async Task Invite_NonOwner_ReturnsForbidden()
    {
        var other = _factory.CreateClientFor(Helpers.OtherUser);
        var id = await CreateApprovedEstablishmentAsync("CR-INV-2");

        var response = await other.PostAsJsonAsync(
            InvitationsUrl(id), new { email = "x@test.com", role = "Manager" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invite_DuplicatePending_ReturnsConflict()
    {
        var owner = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-INV-3");

        (await owner.PostAsJsonAsync(InvitationsUrl(id), new { email = "dup@test.com", role = "HR" }))
            .EnsureSuccessStatusCode();
        var dup = await owner.PostAsJsonAsync(
            InvitationsUrl(id), new { email = "dup@test.com", role = "Manager" });

        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("invitation_already_pending", await CodeOf(dup));
    }

    [Fact]
    public async Task Invite_OwnerRole_ReturnsBadRequest()
    {
        var owner = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-INV-4");

        var response = await owner.PostAsJsonAsync(
            InvitationsUrl(id), new { email = "boss@test.com", role = "Owner" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invitation_role_not_allowed", await CodeOf(response));
    }

    [Fact]
    public async Task Invite_InvalidEmail_ReturnsBadRequest()
    {
        var owner = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-INV-5");

        var response = await owner.PostAsJsonAsync(
            InvitationsUrl(id), new { email = "not-an-email", role = "HR" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invite_NotApproved_ReturnsConflict()
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        // Bare draft (Status=Draft); use admin to bypass the owner check and
        // reach the status guard.
        var draft = await creator.PostAsync("/api/v1/establishments/registration/drafts", null);
        var id = await Helpers.ReadIdAsync(draft);

        var response = await admin.PostAsJsonAsync(
            InvitationsUrl(id), new { email = "early@test.com", role = "HR" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("cannot_edit_in_status", await CodeOf(response));
    }

    [Fact]
    public async Task Invite_EmailAlreadyActiveMember_ReturnsConflict()
    {
        var owner = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-INV-6");

        // Seed a user with a known email + an active membership for that user.
        await Helpers.SeedLocalUserAsync(_factory, "inv-member-x", email: "member-x@test.com");
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.EstablishmentMembers.Add(new EstablishmentMember(
                Guid.NewGuid(), id, "inv-member-x", EstablishmentMemberRole.HR, "seed", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var response = await owner.PostAsJsonAsync(
            InvitationsUrl(id), new { email = "member-x@test.com", role = "Manager" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("email_already_member", await CodeOf(response));
    }

    // -- list ----------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsPending_AndIncludesAcceptedOnDemand()
    {
        var owner = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-INV-7");

        (await owner.PostAsJsonAsync(InvitationsUrl(id), new { email = "p1@test.com", role = "HR" }))
            .EnsureSuccessStatusCode();

        // Seed an Accepted invite directly.
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var accepted = new EstablishmentInvitation(
                Guid.NewGuid(), id, "a1@test.com", EstablishmentMemberRole.Manager,
                new string('a', 64), "seed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));
            accepted.Accept(DateTimeOffset.UtcNow, "someone");
            db.EstablishmentInvitations.Add(accepted);
            await db.SaveChangesAsync();
        }

        var defaultList = (await (await owner.GetAsync(InvitationsUrl(id)))
            .Content.ReadFromJsonAsync<JsonElement>()).DataOf();
        var defaultEmails = defaultList.EnumerateArray()
            .Select(e => e.GetProperty("email").GetString()).ToList();
        Assert.Contains("p1@test.com", defaultEmails);
        Assert.DoesNotContain("a1@test.com", defaultEmails);

        var withAccepted = (await (await owner.GetAsync(InvitationsUrl(id) + "?include=accepted"))
            .Content.ReadFromJsonAsync<JsonElement>()).DataOf();
        var allEmails = withAccepted.EnumerateArray()
            .Select(e => e.GetProperty("email").GetString()).ToList();
        Assert.Contains("p1@test.com", allEmails);
        Assert.Contains("a1@test.com", allEmails);
    }

    // -- revoke --------------------------------------------------------------

    [Fact]
    public async Task Revoke_Pending_SetsRevoked()
    {
        var owner = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-INV-8");
        var create = await owner.PostAsJsonAsync(
            InvitationsUrl(id), new { email = "rev@test.com", role = "HR" });
        var invId = (await create.Content.ReadFromJsonAsync<JsonElement>()).DataOf()
            .GetProperty("id").GetGuid();

        var del = await owner.DeleteAsync($"{InvitationsUrl(id)}/{invId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.EstablishmentInvitations.AsNoTracking().SingleAsync(i => i.Id == invId);
        Assert.Equal(EstablishmentInvitationStatus.Revoked, row.Status);
    }

    [Fact]
    public async Task Revoke_NotPending_ReturnsConflict()
    {
        var owner = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-INV-9");
        var create = await owner.PostAsJsonAsync(
            InvitationsUrl(id), new { email = "rev2@test.com", role = "HR" });
        var invId = (await create.Content.ReadFromJsonAsync<JsonElement>()).DataOf()
            .GetProperty("id").GetGuid();
        (await owner.DeleteAsync($"{InvitationsUrl(id)}/{invId}")).EnsureSuccessStatusCode();

        var again = await owner.DeleteAsync($"{InvitationsUrl(id)}/{invId}");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("invitation_already_used", await CodeOf(again));
    }

    // -- resend --------------------------------------------------------------

    [Fact]
    public async Task Resend_Pending_RotatesTokenAndExpiry()
    {
        var owner = _factory.CreateClientFor(Helpers.Creator);
        var id = await CreateApprovedEstablishmentAsync("CR-INV-10");
        var create = await owner.PostAsJsonAsync(
            InvitationsUrl(id), new { email = "res@test.com", role = "HR" });
        var invId = (await create.Content.ReadFromJsonAsync<JsonElement>()).DataOf()
            .GetProperty("id").GetGuid();

        string hashBefore;
        DateTimeOffset expiresBefore;
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.EstablishmentInvitations.AsNoTracking().SingleAsync(i => i.Id == invId);
            hashBefore = row.TokenHash;
            expiresBefore = row.ExpiresAt;
        }

        var resend = await owner.PostAsync($"{InvitationsUrl(id)}/{invId}/resend", null);
        Assert.Equal(HttpStatusCode.NoContent, resend.StatusCode);

        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.EstablishmentInvitations.AsNoTracking().SingleAsync(i => i.Id == invId);
            Assert.NotEqual(hashBefore, row.TokenHash);
            Assert.True(row.ExpiresAt >= expiresBefore);
            Assert.Equal(EstablishmentInvitationStatus.Pending, row.Status);
        }
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var c) ? c.GetString() : null;
    }
}
