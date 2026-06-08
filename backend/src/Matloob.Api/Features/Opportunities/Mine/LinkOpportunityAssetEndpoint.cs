using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Opportunities.Mine;

/// <summary>
/// <c>POST /api/establishments/me/opportunities/{id}/assets</c> +
/// canonical alias — link an existing <c>Asset</c> to the opportunity.
/// Asset bytes are uploaded via the canonical Assets API; this endpoint
/// only records the relationship.
///
/// <para>
/// Asset must exist, not soft-deleted, and either:
/// </para>
/// <list type="bullet">
///   <item>belong to the calling user (<c>asset.owner_user_id ==
///     caller.sub</c>), or</item>
///   <item>be uploaded by any active member of the establishment (in
///     practice the same check, since current members usually upload via
///     their own sub).</item>
/// </list>
/// </summary>
public sealed class LinkOpportunityAssetEndpoint
    : Endpoint<LinkOpportunityAssetRequest, LinkOpportunityAssetResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public LinkOpportunityAssetEndpoint(
        AppDbContext db,
        ICurrentUser currentUser,
        TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public override void Configure()
    {
        Post(
            "/api/establishments/me/opportunities/{id}/assets",
            "/api/v1/establishments/{establishmentId}/opportunities/{id}/assets");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<LinkOpportunityAssetResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Opportunities"));
        Summary(s =>
        {
            s.Summary = "Link an existing Asset to an opportunity.";
        });
    }

    public override async Task HandleAsync(LinkOpportunityAssetRequest req, CancellationToken ct)
    {
        var establishmentId = await OpportunityWriteGuards.AuthoriseMutationAsync(
            _db, HttpContext, _currentUser.UserId, ct, Infrastructure.Auth.Permissions.Opportunities.Manage);
        if (establishmentId is null) return;

        var oppId = Route<Guid>("id");
        var opportunity = await _db.Opportunities
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == oppId, ct);
        if (opportunity is null
            || opportunity.IssuerEstablishmentId != establishmentId.Value)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var asset = await _db.Assets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == req.AssetId, ct);
        if (asset is null)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                OpportunityErrorCodes.AssetNotFound,
                "Asset does not exist.",
                ct);
            return;
        }

        // Ownership: caller must be the asset uploader (or an admin).
        var isAdmin = MembershipChecks.IsAdmin(HttpContext.User);
        if (!isAdmin && asset.OwnerUserId != _currentUser.UserId)
        {
            await ProblemWriter.WriteAsync(
                HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                OpportunityErrorCodes.AssetNotOwnedByCaller,
                "Asset is not owned by the caller.",
                ct);
            return;
        }

        var now = _clock.GetUtcNow();
        var link = new OpportunityAsset(
            id: Guid.NewGuid(),
            opportunityId: opportunity.Id,
            assetId: asset.Id,
            uploadedByUserId: _currentUser.UserId,
            uploadedAt: now);
        _db.OpportunityAssets.Add(link);
        await _db.SaveChangesAsync(ct);

        var response = new LinkOpportunityAssetResponse
        {
            Id = link.Id,
            AssetId = asset.Id,
            FileName = asset.OriginalFileName,
            ContentType = asset.ContentType,
            SizeBytes = asset.SizeBytes,
            UploadedAt = link.UploadedAt,
        };

        HttpContext.Response.Headers.Location =
            $"/api/v1/establishments/{establishmentId.Value}/opportunities/{opportunity.Id}";
        await Send.ResponseAsync(response, StatusCodes.Status201Created, ct);
    }
}

public sealed class LinkOpportunityAssetRequest
{
    [JsonPropertyName("asset_id")]
    public Guid AssetId { get; init; }
}

public sealed class LinkOpportunityAssetRequestValidator
    : Validator<LinkOpportunityAssetRequest>
{
    public LinkOpportunityAssetRequestValidator()
    {
        RuleFor(x => x.AssetId).NotEmpty();
    }
}

public sealed class LinkOpportunityAssetResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("asset_id")]
    public Guid AssetId { get; init; }

    [JsonPropertyName("file_name")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("content_type")]
    public string ContentType { get; init; } = string.Empty;

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("uploaded_at")]
    public DateTimeOffset UploadedAt { get; init; }
}
