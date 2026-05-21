using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Auth;
using Matloob.Domain.Establishments;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Matloob.Api.Features.Establishments.Registration.UploadDocument;

/// <summary>
/// <c>POST /api/v1/establishments/registration/{id}/documents/authorization-letter</c>
/// — binds an already-uploaded Asset (Purpose=AuthorizationLetter) to the
/// authorization-letter slot on the establishment.
///
/// Re-linking soft-deletes the previous active document AND its underlying
/// Asset (spec §5). FastEndpoints' <c>Send</c> and <c>Route</c> are
/// protected, so the handler call + response mapping must live here rather
/// than in a shared helper — the duplication with the
/// CommercialRegistration endpoint is intentional and tiny.
/// </summary>
public sealed class LinkAuthorizationLetterEndpoint
    : Endpoint<LinkDocumentRequest, LinkDocumentResponse>
{
    private readonly LinkDocumentHandler _handler;

    public LinkAuthorizationLetterEndpoint(LinkDocumentHandler handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Post("/api/v1/establishments/registration/{id}/documents/authorization-letter");
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
            s.Summary = "Link an uploaded AuthorizationLetter Asset to an establishment.";
            s.Description =
                "The Asset must already exist (POST /api/v1/assets) and " +
                "carry Purpose=AuthorizationLetter. Re-linking soft-deletes " +
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
            id, req.AssetId, EstablishmentDocumentType.AuthorizationLetter, ct);

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
                    "The supplied Asset must have Purpose=AuthorizationLetter.",
                    ct);
                return;

            case LinkDocumentHandler.Outcome.Linked:
                var doc = result.Document!;
                HttpContext.Response.Headers.Location =
                    $"/api/v1/establishments/registration/{id}/documents/authorization-letter";
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
