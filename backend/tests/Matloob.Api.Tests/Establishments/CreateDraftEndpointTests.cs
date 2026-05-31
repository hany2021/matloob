using System.Net;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Common;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Establishments;

/// <summary>
/// Integration tests for POST /api/v1/establishments/registration/drafts.
///
/// The endpoint is gated by MatloobPolicies.User — anonymous returns 401,
/// admin-only callers without the matloob_user role return 403. A
/// matloob_user caller creates a Draft row owned by their sub claim.
/// </summary>
public sealed class CreateDraftEndpointTests : IClassFixture<EstablishmentsApiFactory>
{
    private readonly EstablishmentsApiFactory _factory;

    public CreateDraftEndpointTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    private const string Endpoint = "/api/v1/establishments/registration/drafts";

    [Fact]
    public async Task CreateDraft_WithoutUser_ReturnsUnauthorized()
    {
        var client = _factory.CreateClientFor(null);

        var response = await client.PostAsync(Endpoint, content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateDraft_WithAdminOnly_ReturnsForbidden()
    {
        // Admin-only token: authenticated, but missing the matloob_user role
        // that the policy requires. Spec sec 10 -- "create draft" needs the
        // user role; admin role alone is not a superset.
        var user = new TestUser(
            Sub: "admin-only-1",
            Roles: new[] { "matloob_admin" },
            Audiences: new[] { "matloob:admin" });
        var client = _factory.CreateClientFor(user);

        var response = await client.PostAsync(Endpoint, content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateDraft_WithMatloobUser_CreatesDraft()
    {
        var user = new TestUser(
            Sub: "draft-creator-1",
            Roles: new[] { "matloob_user" });
        var client = _factory.CreateClientFor(user);

        var response = await client.PostAsync(Endpoint, content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.StartsWith(
            "/api/v1/establishments/registration/",
            response.Headers.Location?.OriginalString);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement.DataOf();

        var id = root.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal("Draft", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("createdAt").GetDateTimeOffset() > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task CreateDraft_StoresStatusDraftInDatabase()
    {
        var user = new TestUser(
            Sub: "draft-creator-2",
            Roles: new[] { "matloob_user" });
        var client = _factory.CreateClientFor(user);

        var response = await client.PostAsync(Endpoint, content: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var id = (await ReadIdAsync(response));

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Establishments.AsNoTracking().SingleAsync(e => e.Id == id);

        // Status enum -> DB; ignoring query filter is unnecessary because the
        // row was just inserted and is_deleted defaults to false.
        Assert.Equal(EstablishmentStatus.Draft, row.Status);
        Assert.False(row.IsLegacyImport);
        Assert.Null(row.SubmittedAt);
    }

    [Fact]
    public async Task CreateDraft_StoresCreatedByUserIdAsCurrentSub()
    {
        var user = new TestUser(
            Sub: "the-uploader-sub",
            Roles: new[] { "matloob_user" });
        var client = _factory.CreateClientFor(user);

        var response = await client.PostAsync(Endpoint, content: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var id = await ReadIdAsync(response);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Establishments.AsNoTracking().SingleAsync(e => e.Id == id);

        // CreatedByUserId mirrors the JWT sub, which is what makes this user
        // the first Owner once admin approves (spec sec 6.2). Separate from
        // the framework-managed CreatedBy column on BaseAuditableEntity.
        Assert.Equal("the-uploader-sub", row.CreatedByUserId);
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.DataOf().GetProperty("id").GetGuid();
    }
}
