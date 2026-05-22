using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Matloob.Api.Features.Establishments.Common;

/// <summary>
/// Translates a Postgres unique-constraint violation (<c>SQLSTATE 23505</c>)
/// into the application-level (<c>code</c>, <c>detail</c>) pair the
/// establishment endpoints already use for their pre-flight 409 responses.
///
/// Purpose: each establishment mutation does a best-effort pre-flight
/// (e.g. <c>AnyAsync</c> for a duplicate row), which is enough on a quiet
/// system but races on a busy one. Without this translator, two concurrent
/// submits / adds / re-links would let one win and surface the other as a
/// generic 500. With this translator, the second request gets the same 409
/// + machine-readable code the pre-flight produces.
///
/// We translate four specific indexes:
/// <list type="bullet">
///   <item><c>ux_establishments_cr_active</c> -> <see cref="EstablishmentErrorCodes.CrNumberInUse"/></item>
///   <item><c>ux_establishment_documents_slot_active</c> -> <see cref="EstablishmentErrorCodes.DocumentSlotAlreadyExists"/></item>
///   <item><c>ux_establishment_members_pair_active</c> -> <see cref="EstablishmentErrorCodes.MemberAlreadyExists"/></item>
///   <item><c>ux_establishment_change_requests_pending_per_estab</c> -> <see cref="EstablishmentErrorCodes.ChangeRequestAlreadyExists"/></item>
/// </list>
///
/// Unknown <see cref="DbUpdateException"/>s return <c>null</c> from
/// <see cref="TryTranslate"/> so the calling endpoint MUST re-throw — we
/// don't want to swallow real DB errors as 409.
/// </summary>
internal static class UniqueConstraintTranslator
{
    public sealed record Conflict(string Code, string Detail);

    public static Conflict? TryTranslate(DbUpdateException ex)
    {
        var name = GetConstraintName(ex);
        if (name is null) return null;

        return name switch
        {
            "ux_establishments_cr_active" => new Conflict(
                EstablishmentErrorCodes.CrNumberInUse,
                "Commercial registration number is already pending review or approved on another establishment."),

            "ux_establishment_documents_slot_active" => new Conflict(
                EstablishmentErrorCodes.DocumentSlotAlreadyExists,
                "Another active document already occupies this slot. Refresh and try again."),

            "ux_establishment_members_pair_active" => new Conflict(
                EstablishmentErrorCodes.MemberAlreadyExists,
                "User is already an active member of this establishment."),

            "ux_establishment_change_requests_pending_per_estab" => new Conflict(
                EstablishmentErrorCodes.ChangeRequestAlreadyExists,
                "An in-flight ChangeRequest (Draft or PendingReview) already exists for this establishment."),

            // OAO partial-unique indexes — see
            // OpportunityApplicationConfiguration / OfferCancellationRequestConfiguration.
            "ux_opportunity_applications_user_active" => new Conflict(
                Matloob.Api.Features.Opportunities.Common.OpportunityErrorCodes.ApplicationAlreadyExists,
                "You have already applied to this opportunity."),

            "ux_opportunity_applications_establishment_active" => new Conflict(
                Matloob.Api.Features.Opportunities.Common.OpportunityErrorCodes.ApplicationAlreadyExists,
                "Establishment has already applied to this opportunity."),

            "ux_offer_cancellation_requests_open_per_offer" => new Conflict(
                "open_cancellation_request_exists",
                "Another open cancellation request already exists for this offer."),

            _ => null,
        };
    }

    /// <summary>
    /// Walks the inner-exception chain looking for an Npgsql
    /// <see cref="PostgresException"/> with SQLSTATE <c>23505</c>
    /// (unique_violation). Returns the constraint name or null.
    /// </summary>
    private static string? GetConstraintName(DbUpdateException ex)
    {
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is PostgresException pg && pg.SqlState == "23505")
            {
                return pg.ConstraintName;
            }
        }
        return null;
    }
}
