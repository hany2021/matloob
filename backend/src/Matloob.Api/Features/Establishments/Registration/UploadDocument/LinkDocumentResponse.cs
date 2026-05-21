using Matloob.Domain.Establishments;

namespace Matloob.Api.Features.Establishments.Registration.UploadDocument;

public sealed record LinkDocumentResponse(
    Guid EstablishmentId,
    EstablishmentDocumentType DocumentType,
    Guid AssetId,
    DateTimeOffset UploadedAt);
