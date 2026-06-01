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
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Matloob.Api.Features.Profile.UpdateCertificates;

/// <summary>
/// <c>PATCH /api/users/profile/user-certificates</c> (+ canonical) — upsert the
/// user's certificates and delete any not present in the payload (mirrors the
/// legacy <c>UpdateOrCreateUserCertificateController</c>). Multipart bracket
/// arrays (<c>certificates[i][field]</c>, <c>certificates[i][copy]</c> file).
/// Returns the refreshed UserResource in a <c>{ data }</c> envelope.
/// </summary>
public sealed class UpdateCertificatesEndpoint : EndpointWithoutRequest
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;

    public UpdateCertificatesEndpoint(AppDbContext db, ICurrentUser currentUser, IFileStorage storage)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
    }

    public override void Configure()
    {
        Verbs(Http.POST, Http.PATCH);
        Routes("/api/users/profile/user-certificates", "/api/v1/users/profile/user-certificates");
        Policies(MatloobPolicies.User);
        // See UpdateEducationEndpoint for the rationale on not calling
        // AllowFileUploads — short version: precognition pre-validation
        // arrives as JSON without a file and must not 415.
        Description(b => b
            .Produces<DataEnvelope<ProfileResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("Profile"));
        Summary(s => s.Summary = "Upsert the current user's certificates.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        IReadOnlyList<MultipartArrayParser.Item> items;
        if (HttpContext.Request.HasFormContentType)
        {
            var form = await HttpContext.Request.ReadFormAsync(ct);
            items = MultipartArrayParser.Parse(form, "certificates");
        }
        else if (HttpContext.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            items = await MultipartArrayParser.ParseJsonAsync(HttpContext, "certificates", ct);
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

        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (string.IsNullOrWhiteSpace(it.Field("name")))
                AddError($"certificates[{i}].name", "Name is required.");
            if (string.IsNullOrWhiteSpace(it.Field("issued_by")))
                AddError($"certificates[{i}].issued_by", "Issued-by is required.");
            if (!IsDate(it.Field("issued_at")))
                AddError($"certificates[{i}].issued_at", "Issued-at must be a valid date (yyyy-MM-dd).");

            var copy = it.File("copy");
            if (copy is not null && !ProfileAssetSupport.ImageOrPdfContentTypes.Contains(copy.ContentType ?? string.Empty))
                AddError($"certificates[{i}].copy", "Copy must be PDF, JPEG or PNG.");
            if (copy is not null && copy.Length > ProfileAssetSupport.MaxBytes)
                AddError($"certificates[{i}].copy", "Copy exceeds the 2 MB limit.");
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

        var keptIds = new List<Guid>();
        foreach (var it in items)
        {
            var name = it.Field("name")!.Trim();
            var issuedBy = it.Field("issued_by")!.Trim();
            var issuedAt = DateOnly.ParseExact(it.Field("issued_at")!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            UserCertificate? entity = null;
            if (Guid.TryParse(it.Field("id"), out var id))
                entity = await _db.UserCertificates.FirstOrDefaultAsync(c => c.Id == id && c.UserId == user.Id, ct);

            if (entity is null)
            {
                entity = new UserCertificate(Guid.NewGuid(), user.Id, name, issuedBy, issuedAt);
                _db.UserCertificates.Add(entity);
            }
            else
            {
                entity.Update(name, issuedBy, issuedAt);
            }

            if (it.File("copy") is { } copy)
            {
                var assetId = await ProfileAssetSupport.SaveAsync(_db, _storage, sub, copy, AssetVisibility.Public, ct);
                entity.SetCopy(assetId);
            }

            keptIds.Add(entity.Id);
        }

        // Delete certificates not present in the payload (full-replace within set).
        var toDelete = await _db.UserCertificates
            .Where(c => c.UserId == user.Id && !keptIds.Contains(c.Id))
            .ToListAsync(ct);
        _db.UserCertificates.RemoveRange(toDelete);

        await _db.SaveChangesAsync(ct);

        var response = await ProfileReadMapper.BuildAsync(_db, user, ct);
        await Send.OkAsync(new DataEnvelope<ProfileResponse>(response), ct);
    }

    private static bool IsDate(string? value)
        => value is not null && DateOnly.TryParseExact(
            value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
