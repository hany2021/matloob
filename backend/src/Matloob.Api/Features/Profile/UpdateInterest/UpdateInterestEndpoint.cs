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
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Matloob.Api.Features.Profile.UpdateInterest;

/// <summary>
/// <c>PATCH /api/users/profile/interest</c> (+ canonical) — sync the user's
/// professions (interest categories) and upsert their supportive documents
/// (mirrors the legacy <c>UpdateInterestController</c>). Multipart bracket
/// arrays: <c>professions[i][id|other]</c> and
/// <c>supportive_documents[i][id|name|url]</c> + <c>[i][file]</c>.
/// Returns the refreshed UserResource in a <c>{ data }</c> envelope.
/// </summary>
public sealed class UpdateInterestEndpoint : EndpointWithoutRequest
{
    private static readonly IReadOnlySet<string> DocContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/jpg", "application/pdf", "video/mp4" };

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;

    public UpdateInterestEndpoint(AppDbContext db, ICurrentUser currentUser, IFileStorage storage)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
    }

    public override void Configure()
    {
        Verbs(Http.POST, Http.PATCH);
        Routes("/api/users/profile/interest", "/api/v1/users/profile/interest");
        Policies(MatloobPolicies.User);
        // AllowFileUploads omitted — see UpdateEducationEndpoint for why
        // (precognition pre-validation arrives as JSON without files).
        Description(b => b
            .Produces<DataEnvelope<ProfileResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Profile"));
        Summary(s => s.Summary = "Sync professions and upsert supportive documents.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        IReadOnlyList<MultipartArrayParser.Item> professionItems;
        IReadOnlyList<MultipartArrayParser.Item> docItems;
        if (HttpContext.Request.HasFormContentType)
        {
            var form = await HttpContext.Request.ReadFormAsync(ct);
            professionItems = MultipartArrayParser.Parse(form, "professions");
            docItems = MultipartArrayParser.Parse(form, "supportive_documents");
        }
        else if (HttpContext.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            professionItems = await MultipartArrayParser.ParseJsonAsync(HttpContext, "professions", ct);
            docItems = await MultipartArrayParser.ParseJsonAsync(HttpContext, "supportive_documents", ct);
        }
        else
        {
            await Send.ResponseAsync(new ProblemDetails
            {
                Status = StatusCodes.Status415UnsupportedMediaType,
                Title = "Unsupported Media Type",
                Detail = "Expected multipart/form-data or application/json.",
            }, StatusCodes.Status415UnsupportedMediaType, ct);
            return;
        }

        if (professionItems.Count is 0 or > 100)
            AddError("professions", "Between 1 and 100 professions are required.");

        var sub = _currentUser.UserId;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.IdentityId == sub, ct);
        if (user is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // ---- Resolve + validate professions (must be child categories) ----
        var resolved = new List<(Guid CategoryId, string? Other)>();
        for (var i = 0; i < professionItems.Count; i++)
        {
            var it = professionItems[i];
            if (!Guid.TryParse(it.Field("id"), out var catId))
            {
                AddError($"professions[{i}].id", "Profession id is required.");
                continue;
            }

            var category = await _db.OpportunityCategories
                .Where(c => c.Id == catId && c.ParentId != null)
                .Select(c => new { c.Id, c.IsOther })
                .FirstOrDefaultAsync(ct);
            if (category is null)
            {
                AddError($"professions[{i}].id", "Profession not found.");
                continue;
            }

            var other = it.Field("other");
            if (category.IsOther && string.IsNullOrWhiteSpace(other))
                AddError($"professions[{i}].other", "The 'other' value is required for this category.");

            resolved.Add((catId, string.IsNullOrWhiteSpace(other) ? null : other!.Trim()));
        }

        // ---- Validate supportive documents ----
        for (var i = 0; i < docItems.Count; i++)
        {
            var it = docItems[i];
            if (string.IsNullOrWhiteSpace(it.Field("name")))
                AddError($"supportive_documents[{i}].name", "Name is required.");

            var hasId = Guid.TryParse(it.Field("id"), out _);
            var file = it.File("file");
            var url = it.Field("url");
            if (!hasId && file is null && string.IsNullOrWhiteSpace(url))
                AddError($"supportive_documents[{i}].file", "Either a file or a URL is required.");
            if (file is not null && !DocContentTypes.Contains(file.ContentType ?? string.Empty))
                AddError($"supportive_documents[{i}].file", "File must be PDF, JPEG, PNG or MP4.");
            if (file is not null && file.Length > ProfileAssetSupport.MaxBytes)
                AddError($"supportive_documents[{i}].file", "File exceeds the 2 MB limit.");
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

        // ---- Professions: sync (replace) ----
        var existingProfessions = await _db.UserProfessions.Where(p => p.UserId == user.Id).ToListAsync(ct);
        _db.UserProfessions.RemoveRange(existingProfessions);
        foreach (var (categoryId, other) in resolved)
        {
            _db.UserProfessions.Add(new UserProfession(Guid.NewGuid(), user.Id, categoryId, other));
        }

        // ---- Supportive documents: upsert + delete-not-in-set ----
        var keptIds = new List<Guid>();
        foreach (var it in docItems)
        {
            var name = it.Field("name")!.Trim();
            var file = it.File("file");
            var url = it.Field("url");

            SupportiveDocument? entity = null;
            if (Guid.TryParse(it.Field("id"), out var id))
                entity = await _db.SupportiveDocuments.FirstOrDefaultAsync(d => d.Id == id && d.UserId == user.Id, ct);

            if (entity is null)
            {
                entity = new SupportiveDocument(Guid.NewGuid(), user.Id, name,
                    url: string.IsNullOrWhiteSpace(url) ? null : url);
                _db.SupportiveDocuments.Add(entity);
            }
            else
            {
                entity.Rename(name);
            }

            if (file is not null)
            {
                var assetId = await ProfileAssetSupport.SaveAsync(_db, _storage, sub, file, AssetVisibility.Public, ct);
                entity.SetFile(assetId);
            }
            else if (!string.IsNullOrWhiteSpace(url))
            {
                entity.SetUrl(url);
            }

            keptIds.Add(entity.Id);
        }

        var toDelete = await _db.SupportiveDocuments
            .Where(d => d.UserId == user.Id && !keptIds.Contains(d.Id))
            .ToListAsync(ct);
        _db.SupportiveDocuments.RemoveRange(toDelete);

        await _db.SaveChangesAsync(ct);
        await ProfileMutationSupport.RecomputeProfileCompletedAsync(_db, user, ct);
        await _db.SaveChangesAsync(ct);

        var response = await ProfileReadMapper.BuildAsync(_db, user, ct);
        await Send.OkAsync(new DataEnvelope<ProfileResponse>(response), ct);
    }
}
