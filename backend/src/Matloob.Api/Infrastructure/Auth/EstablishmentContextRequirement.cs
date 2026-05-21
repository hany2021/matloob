using Microsoft.AspNetCore.Authorization;

namespace Matloob.Api.Infrastructure.Auth;

/// <summary>
/// Authorization requirement for <see cref="MatloobPolicies.EstablishmentContext"/>.
/// Checked by <see cref="EstablishmentContextHandler"/>.
/// </summary>
internal sealed class EstablishmentContextRequirement : IAuthorizationRequirement
{
}
