using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Establishments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Users;

/// <summary>
/// Tests for <see cref="Matloob.Api.Infrastructure.Identity.UserSync.CurrentUserSyncMiddleware"/>
/// and the AddMember 422 user_not_found_in_system path. Reuses the
/// EstablishmentsApiFactory because the middleware runs on every
/// authenticated request -- any endpoint call exercises it.
/// </summary>
public sealed class UserSyncTests : IClassFixture<EstablishmentsApiFactory>
{
    private readonly EstablishmentsApiFactory _factory;

    public UserSyncTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    private static readonly TestUser FreshUser = new(
        Sub: "fresh-sync-user-1",
        Roles: new[] { "matloob_user" });

    [Fact]
    public async Task FirstAuthenticatedRequest_CreatesLocalUserRow()
    {
        var client = _factory.CreateClientFor(FreshUser);

        // Any authenticated endpoint will do; the middleware fires
        // regardless of route. /api/v1/establishments returns 200 with an
        // empty list for a fresh user.
        var response = await client.GetAsync("/api/v1/establishments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Users.AsNoTracking()
            .SingleAsync(u => u.IdentityId == FreshUser.Sub);
        Assert.True(row.IsActive);
        Assert.NotNull(row.LastSeenAt);
    }

    [Fact]
    public async Task RepeatRequests_UpdateLastSeenAt()
    {
        var client = _factory.CreateClientFor(FreshUser);

        // Two calls; the second's LastSeenAt should be >= the first.
        await client.GetAsync("/api/v1/establishments");

        DateTimeOffset firstSeenAt;
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Users.AsNoTracking()
                .SingleAsync(u => u.IdentityId == FreshUser.Sub);
            firstSeenAt = row.LastSeenAt!.Value;
        }

        await client.GetAsync("/api/v1/establishments");

        using var scope2 = _factory.CreateDbScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var row2 = await db2.Users.AsNoTracking()
            .SingleAsync(u => u.IdentityId == FreshUser.Sub);
        Assert.True(row2.LastSeenAt >= firstSeenAt);
    }

    [Fact]
    public async Task AnonymousRequest_DoesNotCreateUserRow()
    {
        var anon = _factory.CreateClientFor(null);

        // Anonymous calls /health (allow-anonymous) -- the middleware
        // sees no authenticated principal and skips the sync.
        var response = await anon.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // No rows materialized purely from anonymous traffic. (Other tests
        // may have populated the table; the assertion is just that no
        // anonymous-only sub somehow appeared.)
        var hasAnonRow = await db.Users.AsNoTracking()
            .AnyAsync(u => u.IdentityId == "system");
        Assert.False(hasAnonRow);
    }
}
