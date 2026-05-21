using FastEndpoints;
using Matloob.Api.Infrastructure.Identity;

namespace Matloob.Api.Features.Auth.Me;

/// <summary>
/// Returns the current request's authenticated principal: subject id, name,
/// roles, audiences, and a flat list of every claim on the identity.
///
/// Authorization: requires authentication but no specific role. Any valid
/// JWT from NEC IdM works — used by the Angular admin team to confirm tokens
/// and by developers to debug claim shapes.
/// </summary>
public sealed class GetCurrentUserEndpoint : EndpointWithoutRequest<CurrentUserResponse>
{
    private readonly ICurrentUser _currentUser;

    public GetCurrentUserEndpoint(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get("/api/v1/me");
        // No AllowAnonymous() — FastEndpoints' default policy requires
        // authentication. No Policies() / Roles() either — we deliberately
        // accept any authenticated principal here.
        Description(b => b
            .Produces<CurrentUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithName("GetCurrentUser")
            .WithTags("Auth"));
        Summary(s =>
        {
            s.Summary = "Returns the authenticated user's claims, roles, and audiences.";
            s.Description = "Diagnostic endpoint. Useful for confirming the JWT validation pipeline " +
                            "and for the Angular admin to display 'logged in as ...'. " +
                            "Never returns the bearer token itself.";
        });
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        var user = HttpContext.User;
        var identity = user.Identity;

        var claims = user.Claims
            .Select(c => new ClaimPair(c.Type, c.Value))
            .ToList();

        // JwtBearer is configured with RoleClaimType="role" so the canonical
        // claim type is "role" — read it directly rather than via
        // ClaimsIdentity.DefaultRoleClaimType (which can be remapped).
        var roles = user.FindAll("role")
            .Select(c => c.Value)
            .Distinct()
            .ToList();

        var audiences = user.FindAll("aud")
            .Select(c => c.Value)
            .ToList();

        var response = new CurrentUserResponse(
            IsAuthenticated: _currentUser.IsAuthenticated,
            UserId: _currentUser.UserId,
            Name: identity?.Name,
            AuthenticationType: identity?.AuthenticationType,
            Roles: roles,
            Audiences: audiences,
            Claims: claims);

        return Send.OkAsync(response, ct);
    }
}
