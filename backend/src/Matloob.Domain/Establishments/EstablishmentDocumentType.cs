using System.Text.Json.Serialization;

namespace Matloob.Domain.Establishments;

/// <summary>
/// Which slot an <see cref="EstablishmentDocument"/> fills. v1 has exactly
/// two; both are required at submit-for-review
/// (docs/15-establishment-onboarding-spec.md §5).
///
/// Note: a partial unique index on
/// <c>(establishment_id, document_type) WHERE is_deleted = false</c> enforces
/// one active document per slot per establishment.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EstablishmentDocumentType>))]
public enum EstablishmentDocumentType
{
    AuthorizationLetter = 0,
    CommercialRegistration = 1,
}
