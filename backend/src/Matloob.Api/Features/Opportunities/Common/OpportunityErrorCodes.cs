namespace Matloob.Api.Features.Opportunities.Common;

/// <summary>
/// Stable machine-readable error codes for opportunity endpoints. Codes
/// are surfaced in <c>ProblemDetails.extensions.code</c>; the public
/// frontend keys off these strings, never the localized title or detail.
/// </summary>
internal static class OpportunityErrorCodes
{
    public const string EstablishmentContextRequired = "establishment_context_required";
    public const string EstablishmentSuspended       = "establishment_suspended";
    public const string CategoryNotFound             = "opportunity_category_not_found";
    public const string InvalidStatusTransition      = "invalid_opportunity_status_transition";
    public const string AssetNotFound                = "asset_not_found";
    public const string AssetNotOwnedByCaller        = "asset_not_owned_by_caller";
    public const string ApplicationAlreadyExists     = "application_already_exists";
    public const string ApplicationNotApplicable     = "application_not_applicable";
    public const string ApplicationCategoryMismatch  = "application_category_mismatch";
    public const string ApplicationSelfNotAllowed    = "application_self_not_allowed";
}
