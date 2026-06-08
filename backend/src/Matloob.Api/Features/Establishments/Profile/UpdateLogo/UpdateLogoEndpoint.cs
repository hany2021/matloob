using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Profile.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Storage;
using Matloob.Domain.Assets;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Establishments.Profile.UpdateLogo;

/// <summary>
/// <c>PATCH /api/establishments/me/profile/logo</c> (+ canonical
/// <c>/api/v1/establishments/me/profile/logo</c>) — replace the resolved
/// establishment's logo.
///
/// Mirrors Laravel <c>UpdateProfileLogoController</c>
/// (<c>clearMediaCollection('logo')</c> + <c>addMediaFromRequest('logo')</c>).
/// The frontend posts multipart with a <c>logo</c> file and a spoofed
/// <c>_method=PATCH</c>. The image goes through the canonical Asset flow with
/// Public visibility (so the returned URL renders in an &lt;img&gt; without a
/// bearer) and is linked through the polymorphic <c>media</c> table on the
/// <c>logo</c> collection — replace semantics, one logo per establishment.
///
/// Auth: active member (or admin); writes blocked with 423 while Suspended.
/// </summary>
public sealed class UpdateLogoEndpoint : Endpoint<UpdateLogoRequest, DataEnvelope<EstablishmentMeProfileResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _storage;
    private readonly TimeProvider _clock;

    public UpdateLogoEndpoint(
        AppDbContext db, ICurrentUser currentUser, IFileStorage storage, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
        _clock = clock;
    }

    public override void Configure()
    {
        Verbs(Http.POST, Http.PATCH);
        Routes(
            "/api/establishments/me/profile/logo",
            "/api/v1/establishments/me/profile/logo");
        Policies(MatloobPolicies.User);
        AllowFileUploads();
        Description(b => b
            .Accepts<UpdateLogoRequest>("multipart/form-data")
            .Produces<DataEnvelope<EstablishmentMeProfileResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413RequestEntityTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Establishment Profile"));
        Summary(s => s.Summary = "Replace the resolved establishment's logo.");
    }

    public override async Task HandleAsync(UpdateLogoRequest req, CancellationToken ct)
    {
        var establishmentId = await EstablishmentResourceGuards
            .ResolveForWriteAsync(_db, HttpContext, _currentUser.UserId, Infrastructure.Auth.Permissions.Profile.Edit, ct);
        if (establishmentId is null) return;

        var file = req.Logo;
        if (file is null || file.Length == 0)
        {
            AddError(r => r.Logo, "A logo image is required.");
            await Send.ErrorsAsync(StatusCodes.Status422UnprocessableEntity, ct);
            return;
        }
        if (!ProfileAssetSupport.ImageContentTypes.Contains(file.ContentType ?? string.Empty))
        {
            AddError(r => r.Logo, "Logo must be a JPEG or PNG image.");
            await Send.ErrorsAsync(StatusCodes.Status422UnprocessableEntity, ct);
            return;
        }
        if (file.Length > ProfileAssetSupport.MaxBytes)
        {
            AddError(r => r.Logo, "Logo exceeds the 2 MB limit.");
            await Send.ErrorsAsync(StatusCodes.Status413RequestEntityTooLarge, ct);
            return;
        }

        var establishment = await _db.Establishments
            .FirstOrDefaultAsync(e => e.Id == establishmentId.Value, ct);
        if (establishment is null) { await Send.NotFoundAsync(ct); return; }

        var modelId = establishment.Id.ToString();
        await MediaSupport.ClearCollectionAsync(
            _db, EstablishmentProfileReadMapper.ModelType, modelId,
            EstablishmentProfileReadMapper.LogoCollection, ct);
        await MediaSupport.AddUploadAsync(
            _db, _storage,
            EstablishmentProfileReadMapper.ModelType, modelId,
            EstablishmentProfileReadMapper.LogoCollection,
            file, _currentUser.UserId, order: 0,
            AssetVisibility.Public, _clock.GetUtcNow(), ct);
        await _db.SaveChangesAsync(ct);

        var response = await EstablishmentProfileReadMapper.BuildAsync(_db, establishment, ct);
        await Send.OkAsync(new DataEnvelope<EstablishmentMeProfileResponse>(response), ct);
    }
}

public sealed class UpdateLogoRequest
{
    public IFormFile? Logo { get; init; }
}
