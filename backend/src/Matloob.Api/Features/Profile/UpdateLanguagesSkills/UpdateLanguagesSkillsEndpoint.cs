using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Features.Profile.Common;
using Matloob.Api.Features.Profile.Show;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Profile.UpdateLanguagesSkills;

/// <summary>
/// <c>PATCH /api/users/profile/languages-skills</c> (+ canonical) — replace the
/// user's skills and sync their languages. Mirrors the Laravel
/// <c>UpdateOrCreateUserSkillAndLanguageController</c>: skills are deleted and
/// recreated wholesale; languages are synced to the provided reference set.
/// Returns the refreshed UserResource in a <c>{ data }</c> envelope.
/// </summary>
public sealed class UpdateLanguagesSkillsEndpoint
    : Endpoint<UpdateLanguagesSkillsRequest, DataEnvelope<ProfileResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public UpdateLanguagesSkillsEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Verbs(Http.POST, Http.PATCH);
        Routes(
            "/api/users/profile/languages-skills",
            "/api/v1/users/profile/languages-skills");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<DataEnvelope<ProfileResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Profile"));
        Summary(s => s.Summary = "Replace skills and sync languages for the current user.");
    }

    public override async Task HandleAsync(UpdateLanguagesSkillsRequest req, CancellationToken ct)
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

        // Validate referenced languages exist.
        var languageIds = (req.Languages ?? []).Select(l => l.Id!.Value).Distinct().ToList();
        if (languageIds.Count > 0)
        {
            var existing = await _db.Languages
                .Where(l => languageIds.Contains(l.Id))
                .Select(l => l.Id)
                .ToListAsync(ct);
            if (existing.Count != languageIds.Count)
            {
                AddError(r => r.Languages, "One or more languages do not exist.");
                await Send.ErrorsAsync(StatusCodes.Status422UnprocessableEntity, ct);
                return;
            }
        }

        // ---- Skills: full replace ----
        var existingSkills = await _db.UserSkills.Where(s => s.UserId == user.Id).ToListAsync(ct);
        _db.UserSkills.RemoveRange(existingSkills);
        foreach (var s in req.Skills ?? [])
        {
            _db.UserSkills.Add(new UserSkill(
                Guid.NewGuid(), user.Id, s.Name!.Trim(), UserProfileWire.ParseLevel(s.Level!)));
        }

        // ---- Languages: sync (replace) ----
        var existingLanguages = await _db.UserLanguages.Where(l => l.UserId == user.Id).ToListAsync(ct);
        _db.UserLanguages.RemoveRange(existingLanguages);
        foreach (var l in req.Languages ?? [])
        {
            _db.UserLanguages.Add(new UserLanguageProficiency(
                Guid.NewGuid(), user.Id, l.Id!.Value, UserProfileWire.ParseLevel(l.Level!)));
        }

        await _db.SaveChangesAsync(ct);
        await ProfileMutationSupport.RecomputeProfileCompletedAsync(_db, user, ct);
        await _db.SaveChangesAsync(ct);

        var response = await ProfileReadMapper.BuildAsync(_db, user, ct);
        await Send.OkAsync(new DataEnvelope<ProfileResponse>(response), ct);
    }
}
