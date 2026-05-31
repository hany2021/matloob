using FastEndpoints;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Profile.FinishOnboarding;

/// <summary>
/// <c>PATCH /api/users/profile/finish-onboarding</c> +
/// <c>PATCH /api/v1/profile/finish-onboarding</c> — mark the current user as
/// having completed the public-frontend onboarding wizard.
///
/// Mirrors the old Laravel <c>FinishOnboardingController</c> behavior:
/// <c>$user-&gt;update(['onboarded' =&gt; true])</c> then returns 204.
///
/// Idempotent — calling it twice is fine. The <see cref="User.MarkOnboarded"/>
/// method is a no-op when the flag is already true, and the row is only
/// saved when EF detects an actual change.
///
/// Auth: <see cref="MatloobPolicies.User"/> (matloob_user role + matloob:api
/// audience). Anonymous → 401. The CurrentUserSyncMiddleware has already
/// created or refreshed the local row by the time this endpoint runs.
/// </summary>
public sealed class FinishOnboardingEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public FinishOnboardingEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Patch(
            "/api/users/profile/finish-onboarding",
            "/api/v1/profile/finish-onboarding");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Profile"));
        Summary(s =>
        {
            s.Summary = "Mark the current user as having finished onboarding.";
            s.Description =
                "Sets users.onboarded=true for the caller. Idempotent. " +
                "Mirrors the Laravel PATCH /api/users/profile/finish-onboarding.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.IdentityId == sub, ct);
        if (user is null)
        {
            // The sync middleware should always provision the local row
            // before we get here, but be defensive: surface 404 rather than
            // silently 204'ing a non-existent user.
            await Send.NotFoundAsync(ct);
            return;
        }

        user.MarkOnboarded();
        await _db.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}
