using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Domain.Establishments;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Matloob.Api.Features.Establishments.Registration.UploadDocument;

/// <summary>
/// <c>POST /api/v1/establishments/registration/{id}/documents/commercial-registration</c>
/// — binds an already-uploaded Asset (Purpose=CommercialRegistration) to
/// the commercial-registration slot on the establishment.
///
/// Mirror of <see cref="LinkAuthorizationLetterEndpoint"/>; the only
/// difference is the document type and the route segment.
/// </summary>
public sealed class LinkCommercialRegistrationEndpoint
    : Endpoint<LinkDocumentRequest, LinkDocumentResponse>
{
    private readonly LinkDocumentHandler _handler;

    public LinkCommercialRegistrationEndpoint(LinkDocumentHandler handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Post("/api/v1/establishments/registration/{id}/documents/commercial-registration");
        Policies(MatloobPolicies.User);
        Description(b => b
            .Produces<LinkDocumentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "Link an uploaded CommercialRegistration Asset to an establishment.";
            s.Description =
                "The Asset must already exist (POST /api/v1/assets) and " +
                "carry Purpose=CommercialRegistration. Re-linking soft-deletes " +
                "the previous slot and its underlying Asset.";
        });
    }

    public override async Task HandleAsync(LinkDocumentRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");

        if (req.AssetId == Guid.Empty)
        {
            AddError(r => r.AssetId, "AssetId is required.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var result = await _handler.LinkAsync(
            id, req.AssetId, EstablishmentDocumentType.CommercialRegistration, ct);

        switch (result.Outcome)
        {
            case LinkDocumentHandler.Outcome.EstablishmentNotFound:
            case LinkDocumentHandler.Outcome.AssetNotFound:
                await Send.NotFoundAsync(ct);
                return;

            case LinkDocumentHandler.Outcome.Forbidden:
                await Send.ForbiddenAsync(ct);
                return;

            case LinkDocumentHandler.Outcome.EstablishmentNotEditable:
                await WriteConflictAsync(
                    EstablishmentErrorCodes.CannotEditInStatus,
                    "Documents can only be uploaded in Status=Draft or Rejected.",
                    ct);
                return;

            case LinkDocumentHandler.Outcome.AssetNotOwnedByCaller:
                await WriteConflictAsync(
                    EstablishmentErrorCodes.AssetNotOwnedByCaller,
                    "The supplied AssetId is not owned by the caller.",
                    ct);
                return;

            case LinkDocumentHandler.Outcome.AssetPurposeMismatch:
                await WriteConflictAsync(
                    EstablishmentErrorCodes.AssetPurposeMismatch,
                    "The supplied Asset must have Purpose=CommercialRegistration.",
                    ct);
                return;

            case LinkDocumentHandler.Outcome.SlotAlreadyTaken:
                // Race: another link won the slot before our save committed.
                await WriteConflictAsync(
                    EstablishmentErrorCodes.DocumentSlotAlreadyExists,
                    "Another active document already occupies this slot. Refresh and try again.",
                    ct);
                return;

            case LinkDocumentHandler.Outcome.Linked:
                var doc = result.Document!;
                HttpContext.Response.Headers.Location =
                    $"/api/v1/establishments/registration/{id}/documents/commercial-registration";
                await Send.ResponseAsync(
                    new LinkDocumentResponse(
                        EstablishmentId: doc.EstablishmentId,
                        DocumentType: doc.DocumentType,
                        AssetId: doc.AssetId,
                        UploadedAt: doc.UploadedAt),
                    StatusCodes.Status201Created,
                    ct);
                return;
        }
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
