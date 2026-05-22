namespace Matloob.Api.Features.Opportunities.Common;

/// <summary>
/// Stable machine-readable error codes for opportunity endpoints. Codes
/// are surfaced in <c>ProblemDetails.extensions.code</c>; the public
/// frontend keys off these strings, never the localized title or detail.
/// </summary>
internal static class OpportunityErrorCodes
{
    public const string EstablishmentContextRequired = "establishment_context_required";
}
