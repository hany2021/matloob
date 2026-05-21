using Matloob.Domain.Establishments;

namespace Matloob.Api.Features.Establishments.Registration.UpdateBasicInfo;

public sealed record UpdateBasicInfoResponse(
    Guid Id,
    EstablishmentStatus Status,
    DateTimeOffset? UpdatedAt);
