using Matloob.Domain.Establishments;

namespace Matloob.Api.Features.Establishments.Registration.CreateDraft;

/// <summary>
/// Returned from <c>POST /api/v1/establishments/registration/drafts</c>.
/// Deliberately minimal — the caller will follow up with the basic-info
/// PATCH (next commit set) to populate §3 fields.
/// </summary>
public sealed record CreateDraftResponse(
    Guid Id,
    EstablishmentStatus Status,
    DateTimeOffset CreatedAt);
