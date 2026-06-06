using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Establishments.Common;

/// <summary>
/// Branch 1 (permission scaffolding) coverage: the role → permission map and
/// the two <see cref="MembershipChecks"/> lookups that read it.
///
/// Half the cases are pure (<see cref="RolePermissions.PermissionsFor"/> needs
/// no DB); the <c>HasPermissionAsync</c> / <c>GetPermissionsAsync</c> cases
/// seed one active member per role on a shared establishment via the factory's
/// InMemory DbContext, then assert what each role can / can't do.
/// </summary>
public sealed class MembershipChecksTests
    : IClassFixture<EstablishmentsApiFactory>, IAsyncLifetime
{
    private readonly EstablishmentsApiFactory _factory;

    // One establishment, one active member per role. Distinct sub per role so
    // a single query resolves exactly one membership.
    private static readonly Guid Establishment = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private const string OwnerSub = "perm-owner";
    private const string ManagerSub = "perm-manager";
    private const string HrSub = "perm-hr";
    private const string AccountantSub = "perm-accountant";
    private const string CommissionerSub = "perm-commissioner";
    private const string OtherSub = "perm-other";
    private const string NonMemberSub = "perm-nonmember";

    public MembershipChecksTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (db.EstablishmentMembers.Any(m => m.EstablishmentId == Establishment))
        {
            return; // fixture-shared DB; seed once.
        }

        Seed(db, OwnerSub, EstablishmentMemberRole.Owner);
        Seed(db, ManagerSub, EstablishmentMemberRole.Manager);
        Seed(db, HrSub, EstablishmentMemberRole.HR);
        Seed(db, AccountantSub, EstablishmentMemberRole.Accountant);
        Seed(db, CommissionerSub, EstablishmentMemberRole.Commissioner);
        Seed(db, OtherSub, EstablishmentMemberRole.Other);

        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static void Seed(AppDbContext db, string userId, EstablishmentMemberRole role) =>
        db.EstablishmentMembers.Add(new EstablishmentMember(
            id: Guid.NewGuid(),
            establishmentId: Establishment,
            userId: userId,
            role: role,
            addedByUserId: "seed",
            addedAt: DateTimeOffset.UtcNow));

    private async Task<bool> HasAsync(string sub, string permission)
    {
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await MembershipChecks.HasPermissionAsync(db, Establishment, sub, permission, default);
    }

    private async Task<IReadOnlySet<string>> PermsAsync(string sub)
    {
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await MembershipChecks.GetPermissionsAsync(db, Establishment, sub, default);
    }

    // -- RolePermissions.PermissionsFor (pure) -------------------------------

    [Fact]
    public void Owner_HoldsEverySlug()
    {
        Assert.Equal(Permissions.All, RolePermissions.PermissionsFor(EstablishmentMemberRole.Owner));
    }

    [Fact]
    public void Manager_HoldsManageButNotMembersOrFinance()
    {
        var set = RolePermissions.PermissionsFor(EstablishmentMemberRole.Manager);
        Assert.Contains(Permissions.Events.Create, set);
        Assert.Contains(Permissions.Opportunities.Manage, set);
        Assert.Contains(Permissions.ChangeRequests.Submit, set);
        Assert.DoesNotContain(Permissions.Members.Manage, set);
        Assert.DoesNotContain(Permissions.Finance.Edit, set);
    }

    [Fact]
    public void Hr_HoldsHiringSlugsButNotEventsOrProfile()
    {
        var set = RolePermissions.PermissionsFor(EstablishmentMemberRole.HR);
        Assert.Contains(Permissions.Offers.Send, set);
        Assert.Contains(Permissions.Applications.Read, set);
        Assert.Contains(Permissions.Evaluations.Create, set);
        Assert.DoesNotContain(Permissions.Events.Create, set);
        Assert.DoesNotContain(Permissions.Profile.Edit, set);
    }

    [Fact]
    public void Commissioner_HoldsOnlyOfferSlugs()
    {
        var set = RolePermissions.PermissionsFor(EstablishmentMemberRole.Commissioner);
        Assert.Contains(Permissions.Offers.Send, set);
        Assert.Contains(Permissions.Offers.Respond, set);
        Assert.DoesNotContain(Permissions.Profile.Edit, set);
        Assert.DoesNotContain(Permissions.Events.Create, set);
    }

    [Fact]
    public void AccountantAndOther_HoldNothing()
    {
        Assert.Empty(RolePermissions.PermissionsFor(EstablishmentMemberRole.Accountant));
        Assert.Empty(RolePermissions.PermissionsFor(EstablishmentMemberRole.Other));
    }

    // -- HasPermissionAsync (DB-backed) --------------------------------------

    [Fact]
    public async Task HasPermission_Owner_TrueForEverything()
    {
        Assert.True(await HasAsync(OwnerSub, Permissions.Members.Manage));
        Assert.True(await HasAsync(OwnerSub, Permissions.Finance.Edit));
        Assert.True(await HasAsync(OwnerSub, Permissions.Events.Create));
    }

    [Fact]
    public async Task HasPermission_Manager_EventsCreateButNotMembersManage()
    {
        Assert.True(await HasAsync(ManagerSub, Permissions.Events.Create));
        Assert.False(await HasAsync(ManagerSub, Permissions.Members.Manage));
    }

    [Fact]
    public async Task HasPermission_Hr_OfferSendButNotEventsCreate()
    {
        Assert.True(await HasAsync(HrSub, Permissions.Offers.Send));
        Assert.False(await HasAsync(HrSub, Permissions.Events.Create));
    }

    [Fact]
    public async Task HasPermission_Commissioner_OfferRespondButNotProfileEdit()
    {
        Assert.True(await HasAsync(CommissionerSub, Permissions.Offers.Respond));
        Assert.False(await HasAsync(CommissionerSub, Permissions.Profile.Edit));
    }

    [Fact]
    public async Task HasPermission_Other_Nothing()
    {
        Assert.False(await HasAsync(OtherSub, Permissions.Offers.Send));
        Assert.False(await HasAsync(OtherSub, Permissions.Events.Create));
    }

    [Fact]
    public async Task HasPermission_NonMember_False()
    {
        Assert.False(await HasAsync(NonMemberSub, Permissions.Offers.Send));
        Assert.False(await HasAsync("", Permissions.Offers.Send));
    }

    // -- GetPermissionsAsync -------------------------------------------------

    [Fact]
    public async Task GetPermissions_Owner_ReturnsAll()
    {
        Assert.Equal(Permissions.All, await PermsAsync(OwnerSub));
    }

    [Fact]
    public async Task GetPermissions_Manager_MatchesMap()
    {
        Assert.Equal(
            RolePermissions.PermissionsFor(EstablishmentMemberRole.Manager),
            await PermsAsync(ManagerSub));
    }

    [Fact]
    public async Task GetPermissions_NonMember_Empty()
    {
        Assert.Empty(await PermsAsync(NonMemberSub));
    }
}
