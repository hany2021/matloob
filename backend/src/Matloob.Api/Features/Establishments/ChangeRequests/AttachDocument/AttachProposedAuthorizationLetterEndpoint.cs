using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Domain.Establishments;

namespace Matloob.Api.Features.Establishments.ChangeRequests.AttachDocument;

/// <summary>
/// <c>POST /api/v1/establishments/{id}/change-requests/{changeRequestId}/documents/authorization-letter</c>
/// — stashes the AuthorizationLetter asset id on the change request's
/// ProposedAuthorizationLetterAssetId. Live
/// <see cref="EstablishmentDocument"/> rows are not touched here; the
/// approve endpoint swaps them once the admin signs off.
/// </summary>
public sealed class AttachProposedAuthorizationLetterEndpoint
    : Endpoint<AttachProposedDocumentRequest, AttachProposedDocumentResponse>
{
    private readonly AttachProposedDocumentHandler _handler;

    public AttachProposedAuthorizationLetterEndpoint(AttachProposedDocumentHandler handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Post("/api/v1/establishments/{id}/change-requests/{changeRequestId}/documents/authorization-letter");
        Description(b => b
            .Produces<AttachProposedDocumentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "Propose a new AuthorizationLetter asset for a Draft/Rejected ChangeRequest.";
            s.Description =
                "Stores the AssetId on the change request; the live document " +
                "slot stays unchanged until admin approval.";
        });
    }

    public override async Task HandleAsync(AttachProposedDocumentRequest req, CancellationToken ct)
    {
        var establishmentId = Route<Guid>("id");
        var changeRequestId = Route<Guid>("changeRequestId");

        if (req.AssetId == Guid.Empty)
        {
            AddError(r => r.AssetId, "AssetId is required.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var isAdmin = MembershipChecks.IsAdmin(HttpContext.User);
        var result = await _handler.AttachAsync(
            establishmentId,
            changeRequestId,
            req.AssetId,
            EstablishmentDocumentType.AuthorizationLetter,
            isAdmin,
            ct);

        switch (result.Outcome)
        {
            case AttachProposedDocumentHandler.Outcome.EstablishmentNotFound:
            case AttachProposedDocumentHandler.Outcome.ChangeRequestNotFound:
            case AttachProposedDocumentHandler.Outcome.AssetNotFound:
                await Send.NotFoundAsync(ct);
                return;

            case AttachProposedDocumentHandler.Outcome.Forbidden:
                await Send.ForbiddenAsync(ct);
                return;

            case AttachProposedDocumentHandler.Outcome.EstablishmentSuspended:
                await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status423Locked,
                    EstablishmentErrorCodes.EstablishmentSuspended,
                    "Establishment is suspended; mutation actions are blocked.",
                    ct);
                return;

            case AttachProposedDocumentHandler.Outcome.ChangeRequestNotEditable:
                await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                    EstablishmentErrorCodes.CannotEditInStatus,
                    "Proposed documents can only be attached in ChangeRequest Status=Draft or Rejected.",
                    ct);
                return;

            case AttachProposedDocumentHandler.Outcome.AssetNotOwnedByCaller:
                await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                    EstablishmentErrorCodes.AssetNotOwnedByCaller,
                    "The supplied AssetId is not owned by the caller.",
                    ct);
                return;

            case AttachProposedDocumentHandler.Outcome.AssetPurposeMismatch:
                await ProblemWriter.WriteAsync(HttpContext, StatusCodes.Status409Conflict,
                    EstablishmentErrorCodes.AssetPurposeMismatch,
                    "The supplied Asset must have Purpose=AuthorizationLetter.",
                    ct);
                return;

            case AttachProposedDocumentHandler.Outcome.Attached:
                var cr = result.ChangeRequest!;
                HttpContext.Response.Headers.Location =
                    $"/api/v1/establishments/{establishmentId}/change-requests/{changeRequestId}/documents/authorization-letter";
                await Send.ResponseAsync(
                    new AttachProposedDocumentResponse(
                        ChangeRequestId: cr.Id,
                        DocumentType: EstablishmentDocumentType.AuthorizationLetter,
                        AssetId: cr.ProposedAuthorizationLetterAssetId!.Value,
                        UpdatedAt: cr.UpdatedAt),
                    StatusCodes.Status201Created,
                    ct);
                return;
        }
    }
}
