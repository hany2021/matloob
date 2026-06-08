using System.Text.Json;
using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Features.Evaluations.Common;
using Matloob.Api.Features.Evaluations.UserSide;
using Matloob.Api.Features.Opportunities.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Events;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Storage;
using Matloob.Domain.Assets;
using Matloob.Domain.Evaluations;
using Matloob.Domain.Offers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Evaluations.EstablishmentSide;

/// <summary><c>GET /api/establishments/evaluations</c> + canonical.</summary>
public sealed class ListEstablishmentEvaluationsEndpoint
    : EndpointWithoutRequest<IReadOnlyList<EvaluationResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListEstablishmentEvaluationsEndpoint(AppDbContext db, ICurrentUser u)
    { _db = db; _currentUser = u; }

    public override void Configure()
    {
        Get("/api/establishments/evaluations",
            "/api/v1/establishments/{establishmentId}/evaluations");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<IReadOnlyList<EvaluationResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Evaluations"));
        Summary(s => s.Summary = "List evaluations authored by the establishment.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await EstablishmentContextHelper.ResolveAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;

        var isAdmin = MembershipChecks.IsAdmin(HttpContext.User);
        if (!isAdmin)
        {
            var isMember = await MembershipChecks.IsActiveMemberAsync(_db, establishmentId.Value, sub, ct);
            if (!isMember) { await Send.NotFoundAsync(ct); return; }
        }

        var evals = await _db.Evaluations
            .AsNoTracking()
            .Where(e => e.EvaluatorEstablishmentId == establishmentId.Value)
            .OrderByDescending(e => e.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        var responses = new List<EvaluationResponse>(evals.Count);
        foreach (var ev in evals)
        {
            responses.Add(await EvaluationReadMapper.MapAsync(_db, ev, ct));
        }
        await Send.OkAsync(responses, ct);
    }
}

/// <summary><c>GET /api/establishments/evaluations/{id}</c> + canonical.</summary>
public sealed class GetEstablishmentEvaluationEndpoint
    : EndpointWithoutRequest<EvaluationResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetEstablishmentEvaluationEndpoint(AppDbContext db, ICurrentUser u)
    { _db = db; _currentUser = u; }

    public override void Configure()
    {
        Get("/api/establishments/evaluations/{id}",
            "/api/v1/establishments/{establishmentId}/evaluations/{id}");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<EvaluationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Evaluations"));
        Summary(s => s.Summary = "Detail of one evaluation authored by the establishment.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sub = _currentUser.UserId;
        var establishmentId = await EstablishmentContextHelper.ResolveAsync(_db, HttpContext, sub, ct);
        if (establishmentId is null) return;

        var isAdmin = MembershipChecks.IsAdmin(HttpContext.User);
        if (!isAdmin)
        {
            var isMember = await MembershipChecks.IsActiveMemberAsync(_db, establishmentId.Value, sub, ct);
            if (!isMember) { await Send.NotFoundAsync(ct); return; }
        }

        var id = Route<Guid>("id");
        var ev = await _db.Evaluations
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id
                && e.EvaluatorEstablishmentId == establishmentId.Value, ct);
        if (ev is null) { await Send.NotFoundAsync(ct); return; }

        await Send.OkAsync(await EvaluationReadMapper.MapAsync(_db, ev, ct), ct);
    }
}

/// <summary>
/// <c>POST /api/establishments/evaluations</c> + canonical
/// <c>POST /api/v1/establishments/{establishmentId}/evaluations</c>.
///
/// <para>
/// Accepts BOTH content types so the legacy Laravel frontend's
/// multipart/form-data uploads keep working AND new clients can post
/// pure JSON:
/// </para>
/// <list type="bullet">
///   <item><c>application/json</c>: same field set as before
///     (<see cref="CreateEstablishmentEvaluationRequest"/>). No file
///     uploads.</item>
///   <item><c>multipart/form-data</c>: same field set as form fields,
///     plus a repeated <c>uploads[]</c> file part. Each file is saved
///     via <see cref="IFileStorage"/>, an <see cref="Asset"/> row is
///     written, and an <see cref="EvaluationAsset"/> row links it to
///     the new evaluation.</item>
/// </list>
/// </summary>
public sealed class CreateEstablishmentEvaluationEndpoint
    : EndpointWithoutRequest<EvaluationResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IOutboxWriter _outbox;
    private readonly IFileStorage _storage;

    public CreateEstablishmentEvaluationEndpoint(
        AppDbContext db, ICurrentUser u, TimeProvider c, IOutboxWriter o, IFileStorage storage)
    { _db = db; _currentUser = u; _clock = c; _outbox = o; _storage = storage; }

    public override void Configure()
    {
        Post("/api/establishments/evaluations",
             "/api/v1/establishments/{establishmentId}/evaluations");
        Policies(MatloobPolicies.User);
        // No AllowFileUploads / AcceptsAnyContentType — we read the body
        // manually in HandleAsync so any content-type is permitted.
        Description(b => b
            .Produces<EvaluationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithTags("Evaluations"));
        Summary(s => s.Summary = "Establishment submits an evaluation on an offer.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // Read body once; multipart and JSON branches diverge here.
        var (req, uploadedFiles) = await ReadRequestAsync(HttpContext, ct);
        if (req is null)
        {
            await ProblemWriter.WriteAsync(HttpContext,
                StatusCodes.Status400BadRequest,
                "invalid_request_body",
                "Request body could not be parsed as JSON or multipart/form-data.", ct);
            return;
        }

        var sub = _currentUser.UserId;
        var establishmentId = await OpportunityWriteGuards.AuthoriseMutationAsync(_db, HttpContext, sub, ct, Infrastructure.Auth.Permissions.Evaluations.Create);
        if (establishmentId is null) return;

        var offer = await _db.Offers
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == req.OfferId, ct);
        if (offer is null)
        {
            await ProblemWriter.WriteAsync(HttpContext,
                StatusCodes.Status404NotFound,
                EvaluationErrorCodes.OfferNotFound,
                "Offer does not exist.", ct);
            return;
        }

        // The establishment must be either the sender (if applicant is a
        // worker) OR the applicant (if it applied to another opp). Either
        // way, determine the evaluable other-party.
        var application = await _db.OpportunityApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == offer.ApplicationId, ct);
        if (application is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var isSender = offer.SenderEstablishmentId == establishmentId.Value;
        var isApplicantEstablishment = application.ApplicantEstablishmentId == establishmentId.Value;
        if (!isSender && !isApplicantEstablishment)
        {
            await ProblemWriter.WriteAsync(HttpContext,
                StatusCodes.Status404NotFound,
                EvaluationErrorCodes.NotPartyToOffer,
                "Establishment is not a party to this offer.", ct);
            return;
        }

        if (offer.Status is not (OfferStatus.Accepted
            or OfferStatus.WaitingForEvaluation
            or OfferStatus.Completed))
        {
            await ProblemWriter.WriteAsync(HttpContext,
                StatusCodes.Status422UnprocessableEntity,
                EvaluationErrorCodes.OfferNotEvaluable,
                "Offer is not in an evaluable state.", ct);
            return;
        }

        var already = await _db.Evaluations
            .AsNoTracking()
            .AnyAsync(e => e.OfferId == offer.Id
                       && e.EvaluatorEstablishmentId == establishmentId.Value, ct);
        if (already)
        {
            await ProblemWriter.WriteAsync(HttpContext,
                StatusCodes.Status409Conflict,
                EvaluationErrorCodes.AlreadyEvaluated,
                "Establishment has already evaluated this offer.", ct);
            return;
        }

        // Construct the right factory based on who the other party is.
        Evaluation evaluation;
        if (isSender)
        {
            // Establishment evaluates the applicant.
            if (application.ApplicantUserId is { } applicantUser)
            {
                evaluation = Evaluation.ByEstablishmentOfUser(
                    id: Guid.NewGuid(),
                    opportunityId: offer.OpportunityId,
                    offerId: offer.Id,
                    evaluatorEstablishmentId: establishmentId.Value,
                    evaluableUserId: applicantUser,
                    rating: req.Rating,
                    recommendForFutureOpportunities: req.RecommendForFutureOpportunities,
                    matloobEvaluation: req.MatloobEvaluation,
                    matchingPercentage: req.MatchingPercentage,
                    comment: req.Comment,
                    successManagementCriteriaComment: req.SuccessManagementCriteriaComment);
            }
            else
            {
                evaluation = Evaluation.ByEstablishmentOfEstablishment(
                    id: Guid.NewGuid(),
                    opportunityId: offer.OpportunityId,
                    offerId: offer.Id,
                    evaluatorEstablishmentId: establishmentId.Value,
                    evaluableEstablishmentId: application.ApplicantEstablishmentId!.Value,
                    rating: req.Rating,
                    recommendForFutureOpportunities: req.RecommendForFutureOpportunities,
                    matloobEvaluation: req.MatloobEvaluation,
                    matchingPercentage: req.MatchingPercentage,
                    comment: req.Comment,
                    successManagementCriteriaComment: req.SuccessManagementCriteriaComment);
            }
        }
        else
        {
            // Applicant establishment evaluates the sender establishment.
            evaluation = Evaluation.ByEstablishmentOfEstablishment(
                id: Guid.NewGuid(),
                opportunityId: offer.OpportunityId,
                offerId: offer.Id,
                evaluatorEstablishmentId: establishmentId.Value,
                evaluableEstablishmentId: offer.SenderEstablishmentId,
                rating: req.Rating,
                recommendForFutureOpportunities: req.RecommendForFutureOpportunities,
                matloobEvaluation: req.MatloobEvaluation,
                matchingPercentage: req.MatchingPercentage,
                comment: req.Comment,
                successManagementCriteriaComment: req.SuccessManagementCriteriaComment);
        }

        _db.Evaluations.Add(evaluation);

        // Persist any multipart uploads as Assets + EvaluationAsset links
        // (Laravel legacy supported inline uploads[] in the request).
        var now = _clock.GetUtcNow();
        foreach (var file in uploadedFiles)
        {
            if (file.Length == 0) continue;
            var ext = Path.GetExtension(file.FileName ?? string.Empty);
            await using var input = file.OpenReadStream();
            var stored = await _storage.SaveAsync(input, ext, ct);

            var asset = new Asset(
                id: Guid.NewGuid(),
                originalFileName: file.FileName ?? $"upload{ext}",
                storedFileName: stored.StoredFileName,
                contentType: file.ContentType ?? "application/octet-stream",
                sizeBytes: stored.SizeBytes,
                sha256: stored.Sha256Hex,
                relativePath: stored.RelativePath,
                storageDriver: AssetStorageDriver.Local,
                visibility: AssetVisibility.Private,
                purpose: AssetPurpose.Generic,
                ownerUserId: sub,
                ownerEstablishmentId: establishmentId.Value,
                metadataJson: null);
            _db.Assets.Add(asset);
            _db.EvaluationAssets.Add(new EvaluationAsset(
                id: Guid.NewGuid(),
                evaluationId: evaluation.Id,
                assetId: asset.Id,
                uploadedByUserId: sub,
                uploadedAt: now));
        }

        await CreateUserEvaluationEndpoint.MaybeMarkCompleted(_db, offer, ct);

        _outbox.Enqueue(EvaluationEventTypes.Submitted, nameof(Evaluation), evaluation.Id,
            new
            {
                id = evaluation.Id,
                offerId = offer.Id,
                evaluatorEstablishmentId = establishmentId.Value,
                uploadCount = uploadedFiles.Count,
            });
        _outbox.Flush();
        await _db.SaveChangesAsync(ct);

        var response = await EvaluationReadMapper.MapAsync(_db, evaluation, ct);
        HttpContext.Response.Headers.Location =
            $"/api/v1/establishments/{establishmentId.Value}/evaluations/{evaluation.Id}";
        await Send.ResponseAsync(response, StatusCodes.Status201Created, ct);
    }

    /// <summary>
    /// Read the request body once, regardless of whether the caller sent
    /// JSON or multipart/form-data. Returns a parsed
    /// <see cref="CreateEstablishmentEvaluationRequest"/> + the list of
    /// uploaded files (empty for JSON).
    /// </summary>
    private static async Task<(CreateEstablishmentEvaluationRequest? Req, IReadOnlyList<IFormFile> Files)>
        ReadRequestAsync(HttpContext ctx, CancellationToken ct)
    {
        var contentType = ctx.Request.ContentType ?? string.Empty;
        if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            var form = await ctx.Request.ReadFormAsync(ct);
            var req = new CreateEstablishmentEvaluationRequest
            {
                OfferId = ParseGuid(form, "offer_id") ?? Guid.Empty,
                Rating = (byte)(ParseInt(form, "rating") ?? 0),
                RecommendForFutureOpportunities = ParseBool(form, "recommend_for_future_opportunities") ?? false,
                Comment = form["comment"].FirstOrDefault(),
                MatchingPercentage = ParseInt(form, "matching_percentage"),
                SuccessManagementCriteriaComment = form["success_management_criteria_comment"].FirstOrDefault(),
                MatloobEvaluation = (byte?)ParseInt(form, "matloob_evaluation"),
            };
            // Accept both `uploads[]` (Laravel) and `uploads` (.NET) keys.
            var files = new List<IFormFile>();
            foreach (var f in form.Files)
            {
                if (f.Name == "uploads" || f.Name == "uploads[]")
                {
                    files.Add(f);
                }
            }
            return (req, files);
        }

        if (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var req = await ctx.Request.ReadFromJsonAsync<CreateEstablishmentEvaluationRequest>(ct);
                return (req, Array.Empty<IFormFile>());
            }
            catch
            {
                return (null, Array.Empty<IFormFile>());
            }
        }

        // Empty / unknown content type — treat as no body.
        return (new CreateEstablishmentEvaluationRequest(), Array.Empty<IFormFile>());
    }

    private static Guid? ParseGuid(IFormCollection form, string key) =>
        Guid.TryParse(form[key].FirstOrDefault(), out var g) ? g : null;

    private static int? ParseInt(IFormCollection form, string key) =>
        int.TryParse(form[key].FirstOrDefault(), out var i) ? i : null;

    private static bool? ParseBool(IFormCollection form, string key)
    {
        var v = form[key].FirstOrDefault();
        if (string.IsNullOrEmpty(v)) return null;
        if (bool.TryParse(v, out var b)) return b;
        return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CreateEstablishmentEvaluationRequest
{
    [JsonPropertyName("offer_id")]
    public Guid OfferId { get; init; }

    [JsonPropertyName("rating")]
    public byte Rating { get; init; }

    [JsonPropertyName("recommend_for_future_opportunities")]
    public bool RecommendForFutureOpportunities { get; init; }

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("matching_percentage")]
    public int? MatchingPercentage { get; init; }

    [JsonPropertyName("success_management_criteria_comment")]
    public string? SuccessManagementCriteriaComment { get; init; }

    [JsonPropertyName("matloob_evaluation")]
    public byte? MatloobEvaluation { get; init; }
}

public sealed class CreateEstablishmentEvaluationRequestValidator
    : Validator<CreateEstablishmentEvaluationRequest>
{
    public CreateEstablishmentEvaluationRequestValidator()
    {
        RuleFor(x => x.OfferId).NotEmpty();
        RuleFor(x => x.Rating).InclusiveBetween((byte)1, (byte)5);
        RuleFor(x => x.Comment).MaximumLength(500);
        RuleFor(x => x.MatchingPercentage).InclusiveBetween(0, 100)
            .When(x => x.MatchingPercentage.HasValue);
        RuleFor(x => x.MatloobEvaluation).InclusiveBetween((byte)1, (byte)5)
            .When(x => x.MatloobEvaluation.HasValue);
        RuleFor(x => x.SuccessManagementCriteriaComment).MaximumLength(500);
    }
}
