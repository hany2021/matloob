namespace Matloob.Api.Features.Establishments.Registration.UploadDocument;

/// <summary>
/// Wire shape for the two document-link endpoints. The actual upload of the
/// file bytes happens at <c>POST /api/v1/assets</c> (Phase 6); these
/// endpoints only bind a pre-uploaded Asset to an Establishment slot.
/// </summary>
public sealed class LinkDocumentRequest
{
    public Guid AssetId { get; init; }
}
