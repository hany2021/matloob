namespace Matloob.Api.Features.Establishments.Common;

/// <summary>
/// Stable error codes returned in ProblemDetails bodies. Used by the public
/// frontend to switch on machine-readable strings instead of parsing the
/// human-friendly message. Lines up with
/// docs/15-establishment-onboarding-spec.md (`409 cannot_edit_in_status`,
/// `409 cr_number_in_use`, `409 cannot_delete`).
/// </summary>
internal static class EstablishmentErrorCodes
{
    public const string CannotEditInStatus = "cannot_edit_in_status";
    public const string CrNumberInUse = "cr_number_in_use";
    public const string CannotDelete = "cannot_delete";
    public const string DocumentMissing = "document_missing";
    public const string AssetPurposeMismatch = "asset_purpose_mismatch";
    public const string AssetNotFound = "asset_not_found";
    public const string AssetNotOwnedByCaller = "asset_not_owned_by_caller";
    public const string EstablishmentSuspended = "establishment_suspended";
    public const string LastOwnerProtected = "last_owner_protected";
    public const string UserNotFoundInSystem = "user_not_found_in_system";
}
