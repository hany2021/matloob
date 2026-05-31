using System.Globalization;
using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Features.Profile.Common;
using Matloob.Api.Features.Profile.Show;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Storage;
using Matloob.Domain.Assets;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Profile.UpdateEducation;

/// <summary>
/// <c>PATCH /api/users/profile/user-education</c> (+ canonical) — additive
/// upsert of the user's education entries (mirrors the legacy
/// <c>UpdateOrCreateUserEducationController</c>: items with an id update,
/// others insert; omitted rows are NOT deleted). Submitted as multipart with
/// Laravel bracket arrays (<c>education[i][field]</c>, <c>education[i][copy]</c>
/// file). Returns the refreshed UserResource in a <c>{ data }</c> envelope.
/// </summary>
public sealed class UpdateEducationEndpoint : EndpointWithoutRequest
{
    private static readonly string[] Degrees =
        ["doctorate", "master", "bachelor", "diploma", "high_school", "elementary_school"];
    private static readonly int[] GpaSystems = [4, 5, 100];

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;

    public UpdateEducationEndpoint(AppDbContext db, ICurrentUser currentUser, IFileStorage storage)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
    }

    public override void Configure()
    {
        Verbs(Http.POST, Http.PATCH);
        Routes("/api/users/profile/user-education", "/api/v1/users/profile/user-education");
        Policies(MatloobPolicies.User);
        AllowFileUploads();
        Description(b => b
            .Produces<DataEnvelope<ProfileResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Profile"));
        Summary(s => s.Summary = "Upsert the current user's education entries.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var form = await HttpContext.Request.ReadFormAsync(ct);
        var items = MultipartArrayParser.Parse(form, "education");
        var currentYear = DateTime.UtcNow.Year;

        // ---- validate ----
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var degree = it.Field("degree");
            if (degree is null || !Degrees.Contains(degree))
                AddError($"education[{i}].degree", "Invalid degree.");

            if (!int.TryParse(it.Field("gpa_system"), out var gpaSystem) || !GpaSystems.Contains(gpaSystem))
                AddError($"education[{i}].gpa_system", "GPA system must be 4, 5 or 100.");
            else if (!decimal.TryParse(it.Field("gpa"), NumberStyles.Number, CultureInfo.InvariantCulture, out var gpa)
                     || gpa < 0 || gpa > gpaSystem)
                AddError($"education[{i}].gpa", "GPA is out of range.");

            if (!int.TryParse(it.Field("graduation_year"), out var year) || year < 1900 || year > currentYear)
                AddError($"education[{i}].graduation_year", "Invalid graduation year.");

            var copy = it.File("copy");
            if (copy is not null && !ProfileAssetSupport.ImageOrPdfContentTypes.Contains(copy.ContentType ?? string.Empty))
                AddError($"education[{i}].copy", "Copy must be PDF, JPEG or PNG.");
            if (copy is not null && copy.Length > ProfileAssetSupport.MaxBytes)
                AddError($"education[{i}].copy", "Copy exceeds the 2 MB limit.");
        }

        if (ValidationFailures.Count > 0)
        {
            await Send.ErrorsAsync(StatusCodes.Status422UnprocessableEntity, ct);
            return;
        }

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

        foreach (var it in items)
        {
            var degree = UserProfileWire.ParseDegree(it.Field("degree")!);
            var specialization = string.IsNullOrWhiteSpace(it.Field("specialization")) ? null : it.Field("specialization")!.Trim();
            var gpaSystem = int.Parse(it.Field("gpa_system")!);
            var gpa = decimal.Parse(it.Field("gpa")!, CultureInfo.InvariantCulture);
            var year = int.Parse(it.Field("graduation_year")!);

            UserEducation? entity = null;
            if (Guid.TryParse(it.Field("id"), out var id))
                entity = await _db.UserEducation.FirstOrDefaultAsync(e => e.Id == id && e.UserId == user.Id, ct);

            if (entity is null)
            {
                entity = new UserEducation(Guid.NewGuid(), user.Id, degree, specialization, gpaSystem, gpa, year);
                _db.UserEducation.Add(entity);
            }
            else
            {
                entity.Update(degree, specialization, gpaSystem, gpa, year);
            }

            if (it.Bool("delete_copy"))
            {
                entity.SetCopy(null);
            }
            else if (it.File("copy") is { } copy)
            {
                var assetId = await ProfileAssetSupport.SaveAsync(_db, _storage, sub, copy, AssetVisibility.Public, ct);
                entity.SetCopy(assetId);
            }
        }

        await _db.SaveChangesAsync(ct);
        await ProfileMutationSupport.RecomputeProfileCompletedAsync(_db, user, ct);
        await _db.SaveChangesAsync(ct);

        var response = await ProfileReadMapper.BuildAsync(_db, user, ct);
        await Send.OkAsync(new DataEnvelope<ProfileResponse>(response), ct);
    }
}
