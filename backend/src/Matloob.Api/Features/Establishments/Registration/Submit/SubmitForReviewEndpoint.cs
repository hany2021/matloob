using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Matloob.Api.Features.Establishments.Registration.Submit;

/// <summary>
/// <c>POST /api/v1/establishments/registration/{id}/submit</c> — transitions
/// an editable establishment to PendingReview after gating on:
/// - §3.1 required fields all populated (delegated to
///   <see cref="Establishment.SubmitForReview"/> on the aggregate),
/// - both documents linked (active EstablishmentDocument rows for
///   AuthorizationLetter and CommercialRegistration),
/// - CR-number uniqueness across the active lifecycle set (§4).
///
/// Auth: <see cref="MatloobPolicies.User"/> + ownership (CreatedByUserId
/// must match the JWT sub). Non-owner → 403; missing → 404.
///
/// On success, an <see cref="EstablishmentReviewHistory"/> row is appended
/// with action <c>Submitted</c>. Resubmits from Rejected are NOT separately
/// flagged — the action is the same Submitted; the audit timeline lets
/// admins see the prior Rejected entry just by ordering.
/// </summary>
public sealed class SubmitForReviewEndpoint : EndpointWithoutRequest<SubmitForReviewResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public SubmitForReviewEndpoint(AppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public override void Configure()
    {
        Post("/api/v1/establishments/registration/{id}/submit");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<SubmitForReviewResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "Submit a Draft / Rejected establishment for admin review.";
            s.Description =
                "Validates the §3.1 fields, both documents, and CR-number " +
                "uniqueness (PendingReview/Approved/Suspended). On success " +
                "Status becomes PendingReview and an audit row is appended.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");

        var establishment = await _db.Establishments
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (establishment is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!string.Equals(establishment.CreatedByUserId, _currentUser.UserId, StringComparison.Ordinal))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        if (!establishment.IsEditableByCreator)
        {
            await WriteConflictAsync(
                EstablishmentErrorCodes.CannotEditInStatus,
                $"Cannot submit in status '{establishment.Status}'. Allowed: Draft, Rejected.",
                ct);
            return;
        }

        // Documents — both slots must have an active row. The soft-delete
        // query filter excludes any previously-replaced row.
        var presentTypes = await _db.EstablishmentDocuments
            .Where(d => d.EstablishmentId == establishment.Id)
            .Select(d => d.DocumentType)
            .ToListAsync(ct);

        var missingDocs = new List<string>();
        if (!presentTypes.Contains(EstablishmentDocumentType.AuthorizationLetter))
        {
            missingDocs.Add(nameof(EstablishmentDocumentType.AuthorizationLetter));
        }
        if (!presentTypes.Contains(EstablishmentDocumentType.CommercialRegistration))
        {
            missingDocs.Add(nameof(EstablishmentDocumentType.CommercialRegistration));
        }
        if (missingDocs.Count > 0)
        {
            foreach (var d in missingDocs) AddError(d, "Document is required before submission.");
            // Use ProblemDetails code so the public frontend can identify the bucket.
            HttpContext.Response.Headers["x-matloob-error-code"] = EstablishmentErrorCodes.DocumentMissing;
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        // CR-number pre-flight (§4). The partial unique index would also
        // catch this on SaveChanges, but doing it ahead gives a clean 409 +
        // error code rather than a database-violation 500.
        var crNumber = establishment.CommercialRegistrationNumber;
        if (!string.IsNullOrEmpty(crNumber))
        {
            var inUse = await _db.Establishments
                .Where(e => e.Id != establishment.Id)
                .Where(e => e.CommercialRegistrationNumber == crNumber)
                .Where(e => e.Status == EstablishmentStatus.PendingReview
                         || e.Status == EstablishmentStatus.Approved
                         || e.Status == EstablishmentStatus.Suspended)
                .AnyAsync(ct);
            if (inUse)
            {
                await WriteConflictAsync(
                    EstablishmentErrorCodes.CrNumberInUse,
                    "Commercial registration number is already pending review or approved on another establishment.",
                    ct);
                return;
            }
        }

        // Domain transition + required-field gate.
        var now = _clock.GetUtcNow();
        try
        {
            establishment.SubmitForReview(now);
        }
        catch (EstablishmentRequiredFieldsMissingException ex)
        {
            foreach (var field in ex.MissingFields)
            {
                AddError(field, "Required field must be populated before submission.");
            }
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        // Append audit row. Append-only history is NOT soft-deletable, so the
        // row survives even after the establishment is deleted.
        _db.EstablishmentReviewHistory.Add(new EstablishmentReviewHistory(
            id: Guid.NewGuid(),
            establishmentId: establishment.Id,
            action: EstablishmentReviewAction.Submitted,
            occurredAt: now,
            actorUserId: _currentUser.UserId));

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UniqueConstraintTranslator.TryTranslate(ex) is { } conflict)
        {
            // The CR-number pre-flight raced a parallel submit; the partial
            // unique index ux_establishments_cr_active wins the race.
            await WriteConflictAsync(conflict.Code, conflict.Detail, ct);
            return;
        }

        await Send.OkAsync(
            new SubmitForReviewResponse(
                Id: establishment.Id,
                Status: establishment.Status,
                SubmittedAt: establishment.SubmittedAt!.Value),
            ct);
    }

    private async Task WriteConflictAsync(string code, string detail, CancellationToken ct)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Conflict",
            Detail = detail,
            Type = "https://httpstatuses.io/409",
        };
        problem.Extensions["code"] = code;
        HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        HttpContext.Response.ContentType = "application/problem+json";
        await HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken: ct);
    }
}
