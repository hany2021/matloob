using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Features.Establishments.Members.Common;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Common;
using Matloob.Api.Tests.Establishments;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Invitations;

/// <summary>
/// Branch 4 — public invitation preview + authenticated accept, including the
/// two materialization paths (immediate when the accepter's email matches the
/// invite; deferred via the CurrentUserSyncService sweep otherwise) and the
/// establishment-status gates.
/// </summary>
public sealed class AcceptInvitationTests : IClassFixture<EstablishmentsApiFactory>
{
    private readonly EstablishmentsApiFactory _factory;

    public AcceptInvitationTests(EstablishmentsApiFactory factory) => _factory = factory;

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

    /// <summary>Seed an invitation with a known raw token; returns the raw token.</summary>
    private async Task<string> SeedInvitationAsync(
        Guid establishmentId,
        string email,
        EstablishmentMemberRole role,
        DateTimeOffset? expiresAt = null,
        bool accepted = false,
        string? acceptedBy = null)
    {
        var rawToken = $"rawtok-{Guid.NewGuid():N}";
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var invitation = new EstablishmentInvitation(
            id: Guid.NewGuid(),
            establishmentId: establishmentId,
            email: email,
            role: role,
            tokenHash: InvitationSupport.Hash(rawToken),
            invitedByUserId: "seed-owner",
            invitedAt: now,
            expiresAt: expiresAt ?? now.AddDays(7));
        if (accepted)
        {
            invitation.Accept(now, acceptedBy ?? "seed-accepter");
        }
        db.EstablishmentInvitations.Add(invitation);
        await db.SaveChangesAsync();
        return rawToken;
    }

    private static string PreviewUrl(string token) =>
        $"/api/v1/invitations/preview?token={Uri.EscapeDataString(token)}";

    // -- preview -------------------------------------------------------------

    [Fact]
    public async Task Preview_ValidPendingInvite_Returns200()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-ACC-1");
        var token = await SeedInvitationAsync(id, "prev@test.com", EstablishmentMemberRole.Manager);
        var anon = _factory.CreateClientFor(null);

        var response = await anon.GetAsync(PreviewUrl(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JsonElement>()).DataOf();
        Assert.Equal("Acme Events Co", body.GetProperty("establishmentName").GetString());
        Assert.Equal("Manager", body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Preview_UnknownToken_Returns404()
    {
        var anon = _factory.CreateClientFor(null);
        var response = await anon.GetAsync(PreviewUrl("no-such-token"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Preview_Expired_Returns404()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-ACC-2");
        var token = await SeedInvitationAsync(id, "exp@test.com", EstablishmentMemberRole.HR,
            expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        var anon = _factory.CreateClientFor(null);

        var response = await anon.GetAsync(PreviewUrl(token));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Preview_EstablishmentSuspended_Returns404()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-ACC-3");
        var token = await SeedInvitationAsync(id, "susp@test.com", EstablishmentMemberRole.HR);
        var admin = _factory.CreateClientFor(Helpers.Admin);
        (await admin.PostAsJsonAsync($"/api/v1/admin/establishments/{id}/suspend", new { reason = "t" }))
            .EnsureSuccessStatusCode();

        var anon = _factory.CreateClientFor(null);
        var response = await anon.GetAsync(PreviewUrl(token));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -- accept --------------------------------------------------------------

    [Fact]
    public async Task Accept_InviteeEmailMatches_MaterializesImmediately()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-ACC-4");
        var token = await SeedInvitationAsync(id, "newhire@test.com", EstablishmentMemberRole.Manager);
        var invitee = _factory.CreateClientFor(new TestUser(
            Sub: "acc-invitee-1", Roles: new[] { "matloob_user" }, Email: "newhire@test.com"));

        var response = await invitee.PostAsJsonAsync("/api/v1/invitations/accept", new { token });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JsonElement>()).DataOf();
        Assert.Equal(id, body.GetProperty("establishmentId").GetGuid());
        Assert.True(body.GetProperty("membershipMaterialized").GetBoolean());

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var inv = await db.EstablishmentInvitations.AsNoTracking()
            .SingleAsync(i => i.EstablishmentId == id && i.Email == "newhire@test.com");
        Assert.Equal(EstablishmentInvitationStatus.Accepted, inv.Status);
        Assert.NotNull(inv.MaterializedMemberId);
        var member = await db.EstablishmentMembers.AsNoTracking()
            .SingleAsync(m => m.EstablishmentId == id && m.UserId == "acc-invitee-1");
        Assert.Equal(EstablishmentMemberRole.Manager, member.Role);
        Assert.True(member.IsActive);
    }

    [Fact]
    public async Task Accept_EmailMismatch_DefersThenSweepMaterializes()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-ACC-5");
        var token = await SeedInvitationAsync(id, "target@test.com", EstablishmentMemberRole.HR);

        // Accepter's email does NOT match the invite -> deferred.
        var accepter = _factory.CreateClientFor(new TestUser(
            Sub: "acc-accepter", Roles: new[] { "matloob_user" }, Email: "wrong@test.com"));
        var response = await accepter.PostAsJsonAsync("/api/v1/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>())
            .DataOf().GetProperty("membershipMaterialized").GetBoolean());

        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.EstablishmentMembers.AsNoTracking()
                .AnyAsync(m => m.EstablishmentId == id && m.UserId == "acc-accepter"));
        }

        // The real invitee (email matches) makes an authenticated request ->
        // CurrentUserSyncService sweep materializes their membership.
        var invitee = _factory.CreateClientFor(new TestUser(
            Sub: "acc-real-invitee", Roles: new[] { "matloob_user" }, Email: "target@test.com"));
        (await invitee.GetAsync("/api/users/profile")).EnsureSuccessStatusCode();

        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var member = await db.EstablishmentMembers.AsNoTracking()
                .SingleAsync(m => m.EstablishmentId == id && m.UserId == "acc-real-invitee");
            Assert.Equal(EstablishmentMemberRole.HR, member.Role);
            var inv = await db.EstablishmentInvitations.AsNoTracking()
                .SingleAsync(i => i.EstablishmentId == id && i.Email == "target@test.com");
            Assert.Equal(member.Id, inv.MaterializedMemberId);
        }
    }

    [Fact]
    public async Task Accept_Expired_Returns410()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-ACC-6");
        var token = await SeedInvitationAsync(id, "exp2@test.com", EstablishmentMemberRole.HR,
            expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        var invitee = _factory.CreateClientFor(new TestUser(
            Sub: "acc-exp", Roles: new[] { "matloob_user" }, Email: "exp2@test.com"));

        var response = await invitee.PostAsJsonAsync("/api/v1/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task Accept_EstablishmentSuspended_Returns409()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-ACC-7");
        var token = await SeedInvitationAsync(id, "susp2@test.com", EstablishmentMemberRole.HR);
        var admin = _factory.CreateClientFor(Helpers.Admin);
        (await admin.PostAsJsonAsync($"/api/v1/admin/establishments/{id}/suspend", new { reason = "t" }))
            .EnsureSuccessStatusCode();

        var invitee = _factory.CreateClientFor(new TestUser(
            Sub: "acc-susp", Roles: new[] { "matloob_user" }, Email: "susp2@test.com"));
        var response = await invitee.PostAsJsonAsync("/api/v1/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Accept_Twice_SecondReturnsConflict()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-ACC-8");
        var token = await SeedInvitationAsync(id, "twice@test.com", EstablishmentMemberRole.Manager);
        var invitee = _factory.CreateClientFor(new TestUser(
            Sub: "acc-twice", Roles: new[] { "matloob_user" }, Email: "twice@test.com"));

        var first = await invitee.PostAsJsonAsync("/api/v1/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await invitee.PostAsJsonAsync("/api/v1/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invitation_already_used", body.GetProperty("code").GetString());
    }

    // -- deferred materialization + status gate ------------------------------

    [Fact]
    public async Task Materialize_SkipsSuspendedEstablishment_ThenReinstates()
    {
        var id = await CreateApprovedEstablishmentAsync("CR-ACC-9");
        // Seed an already-accepted, unmaterialized invite, then suspend.
        await SeedInvitationAsync(id, "deferred@test.com", EstablishmentMemberRole.Manager,
            accepted: true, acceptedBy: "mat-deferred");
        var admin = _factory.CreateClientFor(Helpers.Admin);
        (await admin.PostAsJsonAsync($"/api/v1/admin/establishments/{id}/suspend", new { reason = "t" }))
            .EnsureSuccessStatusCode();

        var invitee = _factory.CreateClientFor(new TestUser(
            Sub: "mat-deferred", Roles: new[] { "matloob_user" }, Email: "deferred@test.com"));

        // Sweep runs but skips the suspended establishment.
        (await invitee.GetAsync("/api/users/profile")).EnsureSuccessStatusCode();
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.EstablishmentMembers.AsNoTracking()
                .AnyAsync(m => m.EstablishmentId == id && m.UserId == "mat-deferred"));
        }

        // Reinstate -> next sweep materializes.
        (await admin.PostAsync($"/api/v1/admin/establishments/{id}/reinstate", null))
            .EnsureSuccessStatusCode();
        (await invitee.GetAsync("/api/users/profile")).EnsureSuccessStatusCode();
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.EstablishmentMembers.AsNoTracking()
                .AnyAsync(m => m.EstablishmentId == id && m.UserId == "mat-deferred"
                            && m.Role == EstablishmentMemberRole.Manager));
        }
    }
}
