namespace Matloob.Api.Features.Offers.Common;

/// <summary>
/// Stable machine-readable error codes for offer endpoints. Surfaced in
/// <c>ProblemDetails.extensions.code</c>.
/// </summary>
internal static class OfferErrorCodes
{
    public const string EstablishmentSuspended       = "establishment_suspended";
    public const string EstablishmentContextRequired = "establishment_context_required";
    public const string InvalidStatusTransition      = "invalid_offer_status_transition";
    public const string ApplicantNotFound            = "applicant_not_found";
    public const string ApplicantNotVisible          = "applicant_not_visible_to_sender";
    public const string OfferAlreadyExists           = "offer_already_exists_for_applicant";
    public const string SponsorNotFound              = "sponsor_not_found";
    public const string SponsorNotEligible           = "sponsor_not_eligible";
    public const string ForbiddenForCaller           = "forbidden_for_caller";
    public const string OpenCancellationRequestExists = "open_cancellation_request_exists";
    public const string NoOpenCancellationRequest    = "no_open_cancellation_request";
    public const string ProfessionNotFound           = "profession_not_found";
}
