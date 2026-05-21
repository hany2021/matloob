using FastEndpoints;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Profile.Show;

/// <summary>
/// <c>GET /api/v1/profile</c> (canonical) and <c>GET /api/users/profile</c>
/// (Laravel-compat alias) — return the current user's local profile row.
///
/// Auth: any authenticated principal. The
/// <c>CurrentUserSyncMiddleware</c> has already provisioned the row by the
/// time this endpoint runs.
///
/// Response shape: a superset of the local users columns with explicit
/// null placeholders for the many Laravel <c>UserResource</c> relations
/// that don't exist in the new system yet (professions, experiences,
/// certificates, skills, education, city, region, bank, languages,
/// supportive documents, participations, reviews, nationality, media).
/// The placeholders keep the legacy frontend's parser happy without
/// pretending we have the data; downstream migrations will fill them in
/// real values once those features land.
/// </summary>
public sealed class GetProfileEndpoint : EndpointWithoutRequest<ProfileResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetProfileEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        // Two routes for back-compat: the legacy /api/users/profile that
        // the Laravel public frontend already calls, and the new
        // /api/v1/profile that future clients should use. Do not set a
        // single WithName -- ASP.NET requires endpoint names to be
        // globally unique and the alias would collide.
        Get("/api/v1/profile", "/api/users/profile");
        Description(b => b
            .Produces<ProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("Profile"));
        Summary(s =>
        {
            s.Summary = "Current user's profile (local DB row).";
            s.Description =
                "Authenticated. CurrentUserSyncMiddleware has already created " +
                "or refreshed the row by the time this returns. Many Laravel " +
                "UserResource relations are returned as null/[] until the " +
                "matching features land in the new system.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var sub = _currentUser.UserId;
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.IdentityId == sub, ct);
        if (user is null)
        {
            // The middleware should have provisioned this. If it failed
            // (caught + logged inside the sync service), surface 401 so
            // the caller knows their context isn't usable. They can retry.
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var response = new ProfileResponse(
            Id: user.Id,
            IdentityId: user.IdentityId,
            Name: user.Name,
            Email: user.Email,
            Mobile: user.Phone,
            IsActive: user.IsActive,
            CreatedAt: user.CreatedAt,
            LastSeenAt: user.LastSeenAt,
            // Laravel-compat placeholders. Documented as nulls until each
            // feature lands; see docs/40-api-migration-readiness.md.
            Nationality: null,
            City: null,
            Region: null,
            BankAccount: null,
            Languages: Array.Empty<object>(),
            Professions: Array.Empty<object>(),
            Experiences: Array.Empty<object>(),
            Certificates: Array.Empty<object>(),
            Skills: Array.Empty<object>(),
            UserEducation: Array.Empty<object>(),
            SupportiveDocuments: Array.Empty<object>(),
            Participations: Array.Empty<object>(),
            Reviews: Array.Empty<object>(),
            Media: null,
            PhotoAssetId: null);

        await Send.OkAsync(response, ct);
    }
}

/// <summary>
/// Wire shape returned by <c>GET /api/v1/profile</c>. Carries the local
/// users-table columns we have today plus null/[] placeholders for the
/// Laravel UserResource relations we don't carry yet.
/// </summary>
public sealed record ProfileResponse(
    Guid Id,
    string IdentityId,
    string? Name,
    string? Email,
    string? Mobile,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt,
    object? Nationality,
    object? City,
    object? Region,
    object? BankAccount,
    IReadOnlyList<object> Languages,
    IReadOnlyList<object> Professions,
    IReadOnlyList<object> Experiences,
    IReadOnlyList<object> Certificates,
    IReadOnlyList<object> Skills,
    IReadOnlyList<object> UserEducation,
    IReadOnlyList<object> SupportiveDocuments,
    IReadOnlyList<object> Participations,
    IReadOnlyList<object> Reviews,
    object? Media,
    Guid? PhotoAssetId);
