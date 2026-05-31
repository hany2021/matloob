using System.Globalization;
using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Features.Profile.Common;
using Matloob.Api.Features.Profile.Show;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Profile.UpdateExperiences;

/// <summary>
/// <c>PATCH /api/users/profile/user-experiences</c> (+ canonical) — set the
/// user's years_of_experience and replace their experience set. Mirrors the
/// legacy <c>UpdateOrCreateUserExperienceController</c> net effect. Returns the
/// refreshed UserResource in a <c>{ data }</c> envelope.
/// </summary>
public sealed class UpdateExperiencesEndpoint
    : Endpoint<UpdateExperiencesRequest, DataEnvelope<ProfileResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public UpdateExperiencesEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Verbs(Http.POST, Http.PATCH);
        Routes(
            "/api/users/profile/user-experiences",
            "/api/v1/users/profile/user-experiences");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<DataEnvelope<ProfileResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Profile"));
        Summary(s => s.Summary = "Set years_of_experience and replace the experience set.");
    }

    public override async Task HandleAsync(UpdateExperiencesRequest req, CancellationToken ct)
    {
        if (ProfileMutationSupport.IsPrecognitive(HttpContext))
        {
            await Send.NoContentAsync(ct);
            return;
        }

        var sub = _currentUser.UserId;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.IdentityId == sub, ct);
        if (user is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        user.SetYearsOfExperience(req.YearsOfExperience!.Value);

        var existing = await _db.UserExperiences.Where(e => e.UserId == user.Id).ToListAsync(ct);
        _db.UserExperiences.RemoveRange(existing);

        foreach (var e in req.Experiences ?? [])
        {
            var current = e.Current ?? false;
            var from = ParseDate(e.From)!.Value;
            DateOnly? to = current ? null : ParseDate(e.To);

            _db.UserExperiences.Add(new UserExperience(
                Guid.NewGuid(), user.Id,
                e.Company!.Trim(), e.Position!.Trim(),
                from, to, current,
                string.IsNullOrWhiteSpace(e.Description) ? null : e.Description.Trim(),
                UserProfileWire.ParseExperienceType(e.Type!)));
        }

        await _db.SaveChangesAsync(ct);
        await ProfileMutationSupport.RecomputeProfileCompletedAsync(_db, user, ct);
        await _db.SaveChangesAsync(ct);

        var response = await ProfileReadMapper.BuildAsync(_db, user, ct);
        await Send.OkAsync(new DataEnvelope<ProfileResponse>(response), ct);
    }

    private static DateOnly? ParseDate(string? value)
        => string.IsNullOrEmpty(value)
            ? null
            : DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
