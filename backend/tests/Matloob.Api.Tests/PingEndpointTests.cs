using System.Net;
using Matloob.Api.Tests.Auth;

namespace Matloob.Api.Tests;

public sealed class PingEndpointTests : IClassFixture<MatloobApiFactory>
{
    private readonly MatloobApiFactory _factory;

    public PingEndpointTests(MatloobApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Ping_WithoutUser_ReturnsUnauthorized()
    {
        var client = _factory.CreateClientFor(null);

        var response = await client.GetAsync("/api/v1/system/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ping_WithAuthenticatedUserMissingRole_ReturnsForbidden()
    {
        // Authenticated, but no matloob_user role → policy denies → 403.
        var user = new TestUser(Sub: "user-without-roles", Roles: Array.Empty<string>());
        var client = _factory.CreateClientFor(user);

        var response = await client.GetAsync("/api/v1/system/ping");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ping_WithMatloobUserRole_ReturnsOk()
    {
        var user = new TestUser(
            Sub: "user-with-role",
            Roles: new[] { "matloob_user" });
        var client = _factory.CreateClientFor(user);

        var response = await client.GetAsync("/api/v1/system/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", body);
    }

    [Fact]
    public async Task Ping_WithMatloobAdminRoleOnly_ReturnsForbidden()
    {
        // Admin role alone does NOT satisfy MatloobPolicies.User (which
        // requires matloob_user specifically) — guards against the "admin
        // is automatically a super-set of user" assumption.
        var user = new TestUser(
            Sub: "admin-only",
            Roles: new[] { "matloob_admin" });
        var client = _factory.CreateClientFor(user);

        var response = await client.GetAsync("/api/v1/system/ping");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
