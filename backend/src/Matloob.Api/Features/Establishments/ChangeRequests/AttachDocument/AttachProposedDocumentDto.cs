using Matloob.Domain.Establishments;

namespace Matloob.Api.Features.Establishments.ChangeRequests.AttachDocument;

public sealed class AttachProposedDocumentRequest
{
    public Guid AssetId { get; init; }
}

public sealed record AttachProposedDocumentResponse(
    Guid ChangeRequestId,
    EstablishmentDocumentType DocumentType,
    Guid AssetId,
    DateTimeOffset? UpdatedAt);
