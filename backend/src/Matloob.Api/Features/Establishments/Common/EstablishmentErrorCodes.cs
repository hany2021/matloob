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
    public const string ChangeRequestAlreadyExists = "change_request_already_exists";
    public const string ChangeRequestEmpty = "change_request_empty";

    /// <summary>
    /// One active <see cref="Matloob.Domain.Establishments.EstablishmentDocument"/>
    /// already exists for the (establishment_id, document_type) pair.
    /// Surfaced by the DB-level constraint <c>ux_establishment_documents_slot_active</c>
    /// when a concurrent re-link beats the application-side soft-delete-then-insert pair.
    /// </summary>
    public const string DocumentSlotAlreadyExists = "document_slot_already_exists";

    /// <summary>
    /// An active <see cref="Matloob.Domain.Establishments.EstablishmentMember"/>
    /// row already exists for the (establishment_id, user_id) pair.
    /// Surfaced both by the application-side duplicate check in AddMember
    /// and by the DB-level <c>ux_establishment_members_pair_active</c>
    /// constraint when two requests race.
    /// </summary>
    public const string MemberAlreadyExists = "member_already_exists";

    // ---- Invitation flow (invite-by-email) --------------------------------

    /// <summary>A Pending invite already exists for this (establishment, email).</summary>
    public const string InvitationAlreadyPending = "invitation_already_pending";

    /// <summary>The email already belongs to an active member of the establishment.</summary>
    public const string EmailAlreadyMember = "email_already_member";

    /// <summary>The invite token was already accepted / revoked / expired (accept race lost).</summary>
    public const string InvitationAlreadyUsed = "invitation_already_used";

    /// <summary>Owner tried to invite at the Owner role (only the approval flow grants Owner).</summary>
    public const string InvitationRoleNotAllowed = "invitation_role_not_allowed";
}
