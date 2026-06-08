using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Matloob.Api.Tests.Auth;

/// <summary>
/// Test-only authentication handler. Replaces JwtBearer inside WebApplicationFactory
/// so integration tests never depend on a real IdentityServer.
///
/// Caller sets the <see cref="HeaderName"/> request header to a JSON payload
/// matching <see cref="TestUser"/>; the handler turns it into a ClaimsPrincipal.
/// Absence of the header returns NoResult (which becomes 401 on protected
/// endpoints — exactly the anonymous-caller scenario we want to test).
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string HeaderName = "X-Test-User";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var raw) || raw.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        TestUser? user;
        try
        {
            user = JsonSerializer.Deserialize<TestUser>(raw.ToString()!,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            return Task.FromResult(AuthenticateResult.Fail($"Invalid {HeaderName} JSON: {ex.Message}"));
        }

        if (user is null || string.IsNullOrEmpty(user.Sub))
        {
            return Task.FromResult(AuthenticateResult.Fail($"{HeaderName} missing sub"));
        }

        var claims = new List<Claim>
        {
            new("sub", user.Sub),
        };

        if (!string.IsNullOrEmpty(user.Email))
        {
            claims.Add(new Claim("email", user.Email));
        }

        foreach (var role in user.Roles ?? Array.Empty<string>())
        {
            claims.Add(new Claim("role", role));
        }

        foreach (var aud in user.Audiences ?? Array.Empty<string>())
        {
            claims.Add(new Claim("aud", aud));
        }

        // NameClaimType="sub" + RoleClaimType="role" must match the production
        // JwtBearer setup — otherwise User.IsInRole(...) checks against a
        // different claim type and policies misbehave.
        var identity = new ClaimsIdentity(claims, SchemeName, nameType: "sub", roleType: "role");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>JSON shape sent through the X-Test-User header.</summary>
public sealed record TestUser(
    string Sub,
    IReadOnlyList<string>? Roles = null,
    IReadOnlyList<string>? Audiences = null,
    string? Email = null);
