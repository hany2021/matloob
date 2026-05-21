using FastEndpoints;
using Matloob.Domain.Assets;
using Microsoft.AspNetCore.Http;

namespace Matloob.Api.Features.Assets.UploadAsset;

/// <summary>
/// Multipart upload payload for <see cref="UploadAssetEndpoint"/>.
///
/// FastEndpoints binds <c>IFormFile</c> via standard ASP.NET Core model
/// binding; the scalar fields come from form values, not JSON.
/// </summary>
public sealed class UploadAssetRequest
{
    /// <summary>The uploaded file. Required.</summary>
    public IFormFile File { get; init; } = default!;

    /// <summary>Why the file is being uploaded. Defaults to <see cref="AssetPurpose.Generic"/>.</summary>
    public AssetPurpose Purpose { get; init; } = AssetPurpose.Generic;

    /// <summary>Who can download. Defaults to <see cref="AssetVisibility.Private"/>.</summary>
    public AssetVisibility Visibility { get; init; } = AssetVisibility.Private;

    /// <summary>
    /// Establishment the upload is "for", if any. Membership is NOT validated
    /// here — that hook lands when EstablishmentMember exists (Phase 8). For
    /// now any authenticated user can claim any establishment id.
    /// </summary>
    [BindFrom("ownerEstablishmentId")]
    public Guid? OwnerEstablishmentId { get; init; }
}
