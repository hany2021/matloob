using FastEndpoints;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Profile.DeleteItems;

/// <summary>
/// <c>DELETE /api/users/profile/user-skills/{id}</c> — remove one skill owned
/// by the current user. The frontend's delete hooks ignore the body and
/// refetch the profile, so a 204 is sufficient. Soft-deletes via the
/// interceptor.
/// </summary>
public sealed class DeleteSkillEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public DeleteSkillEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Delete("/api/users/profile/user-skills/{id}", "/api/v1/users/profile/user-skills/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Profile"));
        Summary(s => s.Summary = "Delete one of the current user's skills.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var userId = await ProfileItemDeletes.ResolveUserIdAsync(_db, _currentUser, ct);
        if (userId is null) { await Send.NotFoundAsync(ct); return; }

        var entity = await _db.UserSkills
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (entity is null) { await Send.NotFoundAsync(ct); return; }

        _db.UserSkills.Remove(entity);
        await _db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}

/// <summary><c>DELETE /api/users/profile/user-experiences/{id}</c>.</summary>
public sealed class DeleteExperienceEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public DeleteExperienceEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Delete("/api/users/profile/user-experiences/{id}", "/api/v1/users/profile/user-experiences/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Profile"));
        Summary(s => s.Summary = "Delete one of the current user's experiences.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var userId = await ProfileItemDeletes.ResolveUserIdAsync(_db, _currentUser, ct);
        if (userId is null) { await Send.NotFoundAsync(ct); return; }

        var entity = await _db.UserExperiences
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId, ct);
        if (entity is null) { await Send.NotFoundAsync(ct); return; }

        _db.UserExperiences.Remove(entity);
        await _db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}

/// <summary><c>DELETE /api/users/profile/user-certificates/{id}</c>.</summary>
public sealed class DeleteCertificateEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public DeleteCertificateEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Delete("/api/users/profile/user-certificates/{id}", "/api/v1/users/profile/user-certificates/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Profile"));
        Summary(s => s.Summary = "Delete one of the current user's certificates.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var userId = await ProfileItemDeletes.ResolveUserIdAsync(_db, _currentUser, ct);
        if (userId is null) { await Send.NotFoundAsync(ct); return; }

        var entity = await _db.UserCertificates
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
        if (entity is null) { await Send.NotFoundAsync(ct); return; }

        _db.UserCertificates.Remove(entity);
        await _db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}

internal static class ProfileItemDeletes
{
    public static async Task<Guid?> ResolveUserIdAsync(
        AppDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var sub = currentUser.UserId;
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.IdentityId == sub)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
        return user;
    }
}
