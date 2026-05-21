using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Domain.Establishments;

namespace Matloob.Api.Features.Establishments.ChangeRequests.AttachDocument;

/// <summary>
/// Mirror of <see cref="AttachProposedAuthorizationLetterEndpoint"/> for the
/// commercial-registration slot. The handler is shared; only the route and
/// document type differ.
/// </summary>
public sealed class AttachProposedCommercialRegistrationEndpoint
    : Endpoint<AttachProposedDocumentRequest, AttachProposedDocumentResponse>
{
    private readonly AttachProposedDocumentHandler _handler;

    public AttachProposedCommercialRegistrationEndpoint(AttachProposedDocumentHandler handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Post("/api/v1/establishments/{id}/change-requests/{changeRequestId}/documents/commercial-registration");
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
            s.Summary = "Propose a new CommercialRegistration asset for a Draft/Rejected ChangeRequest.";
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
            EstablishmentDocumentType.CommercialRegistration,
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
                    "The supplied Asset must have Purpose=CommercialRegistration.",
                    ct);
                return;

            case AttachProposedDocumentHandler.Outcome.Attached:
                var cr = result.ChangeRequest!;
                HttpContext.Response.Headers.Location =
                    $"/api/v1/establishments/{establishmentId}/change-requests/{changeRequestId}/documents/commercial-registration";
                await Send.ResponseAsync(
                    new AttachProposedDocumentResponse(
                        ChangeRequestId: cr.Id,
                        DocumentType: EstablishmentDocumentType.CommercialRegistration,
                        AssetId: cr.ProposedCommercialRegistrationAssetId!.Value,
                        UpdatedAt: cr.UpdatedAt),
                    StatusCodes.Status201Created,
                    ct);
                return;
        }
    }
}
