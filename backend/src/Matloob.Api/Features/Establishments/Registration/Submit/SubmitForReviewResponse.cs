using Matloob.Domain.Establishments;

namespace Matloob.Api.Features.Establishments.Registration.Submit;

public sealed record SubmitForReviewResponse(
    Guid Id,
    EstablishmentStatus Status,
    DateTimeOffset SubmittedAt);
