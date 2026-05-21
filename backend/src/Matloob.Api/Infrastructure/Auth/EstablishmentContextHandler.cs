using Microsoft.AspNetCore.Authorization;

namespace Matloob.Api.Infrastructure.Auth;

/// <summary>
/// Handler for <see cref="EstablishmentContextRequirement"/>.
///
/// Phase 4 implementation: succeeds if the request carries a non-empty
/// <c>X-Commissioner-UUID</c> header. Role + authentication are still
/// enforced by the policy itself (RequireAuthenticatedUser + RequireRole).
///
/// TODO (Phase 8 — EstablishmentOnboarding):
///   - Parse the header value as a <see cref="Guid"/>; fail if invalid.
///   - Look up an active EstablishmentMember for (UserId, EstablishmentId).
///   - Verify Establishment.Status ∈ { Approved, Suspended-for-reads }.
///   - On success, stash the resolved EstablishmentMember on
///     <see cref="HttpContext.Items"/> so downstream handlers can read it
///     without re-querying.
/// Until the EstablishmentMember entity exists, presence-only is the safe
/// gate — no business endpoint uses this policy yet.
/// </summary>
internal sealed class EstablishmentContextHandler
    : AuthorizationHandler<EstablishmentContextRequirement>
{
    public const string HeaderName = "X-Commissioner-UUID";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public EstablishmentContextHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        EstablishmentContextRequirement requirement)
    {
        var http = _httpContextAccessor.HttpContext;

        if (http is null)
        {
            return Task.CompletedTask; // no context → no succeed → 403
        }

        if (!http.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return Task.CompletedTask;
        }

        var header = values.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
