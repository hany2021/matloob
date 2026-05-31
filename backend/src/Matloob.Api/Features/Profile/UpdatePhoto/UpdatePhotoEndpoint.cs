using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Features.Profile.Common;
using Matloob.Api.Features.Profile.Show;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Storage;
using Matloob.Domain.Assets;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Profile.UpdatePhoto;

/// <summary>
/// <c>POST /api/users/profile/photo</c> (+ canonical) — set the current user's
/// profile photo. The frontend posts multipart with a <c>photo</c> file and a
/// spoofed <c>_method=patch</c>. The image is stored via the Asset flow
/// (Public visibility so the returned URL renders in an &lt;img&gt; without a
/// bearer token) and linked onto the user. Returns the refreshed UserResource
/// wrapped in a <c>{ data }</c> envelope.
/// </summary>
public sealed class UpdatePhotoEndpoint : Endpoint<UpdatePhotoRequest, DataEnvelope<ProfileResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;

    public UpdatePhotoEndpoint(AppDbContext db, ICurrentUser currentUser, IFileStorage storage)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
    }

    public override void Configure()
    {
        Verbs(Http.POST, Http.PATCH);
        Routes("/api/users/profile/photo", "/api/v1/users/profile/photo");
        Policies(MatloobPolicies.User);
        AllowFileUploads();
        Description(b => b
            .Accepts<UpdatePhotoRequest>("multipart/form-data")
            .Produces<DataEnvelope<ProfileResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413RequestEntityTooLarge)
            .WithTags("Profile"));
        Summary(s => s.Summary = "Upload/replace the current user's profile photo.");
    }

    public override async Task HandleAsync(UpdatePhotoRequest req, CancellationToken ct)
    {
        var file = req.Photo;
        if (file is null || file.Length == 0)
        {
            AddError(r => r.Photo, "A photo file is required.");
            await Send.ErrorsAsync(StatusCodes.Status422UnprocessableEntity, ct);
            return;
        }

        if (!ProfileAssetSupport.ImageContentTypes.Contains(file.ContentType ?? string.Empty))
        {
            AddError(r => r.Photo, "Photo must be a JPEG or PNG image.");
            await Send.ErrorsAsync(StatusCodes.Status422UnprocessableEntity, ct);
            return;
        }

        if (file.Length > ProfileAssetSupport.MaxBytes)
        {
            AddError(r => r.Photo, "Photo exceeds the 2 MB limit.");
            await Send.ErrorsAsync(StatusCodes.Status413RequestEntityTooLarge, ct);
            return;
        }

        var sub = _currentUser.UserId;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.IdentityId == sub, ct);
        if (user is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var assetId = await ProfileAssetSupport.SaveAsync(
            _db, _storage, sub, file, AssetVisibility.Public, ct);
        user.SetPhoto(assetId);

        await _db.SaveChangesAsync(ct);

        var response = await ProfileReadMapper.BuildAsync(_db, user, ct);
        await Send.OkAsync(new DataEnvelope<ProfileResponse>(response), ct);
    }
}

public sealed class UpdatePhotoRequest
{
    public IFormFile? Photo { get; init; }
}
