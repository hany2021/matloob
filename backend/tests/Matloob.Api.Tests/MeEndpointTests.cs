using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Common;

namespace Matloob.Api.Tests;

public sealed class MeEndpointTests : IClassFixture<MatloobApiFactory>
{
    private readonly MatloobApiFactory _factory;

    public MeEndpointTests(MatloobApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Me_WithoutUser_ReturnsUnauthorized()
    {
        var client = _factory.CreateClientFor(null);

        var response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithAuthenticatedUser_ReturnsOkAndExposesClaims()
    {
        // /api/v1/me accepts ANY authenticated principal — no role required.
        var user = new TestUser(
            Sub: "test-subject-123",
            Roles: new[] { "matloob_user", "matloob_admin" },
            Audiences: new[] { "matloob:api", "matloob:admin" });
        var client = _factory.CreateClientFor(user);

        var response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement.DataOf();

        Assert.True(root.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal("test-subject-123", root.GetProperty("userId").GetString());

        var roles = root.GetProperty("roles").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("matloob_user", roles);
        Assert.Contains("matloob_admin", roles);

        var audiences = root.GetProperty("audiences").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("matloob:api", audiences);
        Assert.Contains("matloob:admin", audiences);

        // Claims list should at minimum contain the sub claim we sent.
        var claims = root.GetProperty("claims").EnumerateArray();
        Assert.Contains(claims, c =>
            c.GetProperty("type").GetString() == "sub" &&
            c.GetProperty("value").GetString() == "test-subject-123");
    }

    [Fact]
    public async Task Me_AnonymousButProtectedRouteAttempt_ReturnsUnauthorized()
    {
        // Same as above but with an empty TestUser header — handler returns
        // NoResult, default scheme challenges, response is 401.
        var client = _factory.CreateClientFor(null);
        client.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, "");

        var response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
