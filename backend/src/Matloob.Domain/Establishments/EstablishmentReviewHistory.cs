using Matloob.Domain.Common;

namespace Matloob.Domain.Establishments;

/// <summary>
/// Append-only audit row for everything that ever happened to an
/// <see cref="Establishment"/>. Captures status transitions, change-request
/// lifecycle, and member additions/removals.
///
/// Deliberately inherits <see cref="BaseEntity{TId}"/> (NOT
/// <see cref="BaseAuditableEntity{TId}"/>) so the row is exempt from:
/// - the global soft-delete query filter (history must be visible forever),
/// - the SoftDeleteInterceptor (no Remove() rewrite),
/// - the AuditingInterceptor's CreatedAt/UpdatedAt churn (this table IS the audit).
///
/// <see cref="OccurredAt"/> is the canonical timestamp and is set by the
/// writer, not an interceptor.
/// </summary>
public sealed class EstablishmentReviewHistory : BaseEntity<Guid>
{
    public Guid EstablishmentId { get; private set; }

    /// <summary>Set only when the row was produced by a ChangeRequest transition.</summary>
    public Guid? ChangeRequestId { get; private set; }

    public EstablishmentReviewAction Action { get; private set; }

    /// <summary>
    /// Sub claim of the end-user who triggered the event, if any (e.g.
    /// Submitted by the creator, MemberAdded by an Owner). Null for
    /// admin-driven transitions; see <see cref="ActorAdminId"/>.
    /// </summary>
    public string? ActorUserId { get; private set; }

    /// <summary>
    /// Sub claim of the admin who triggered the event, if any (Approve,
    /// Reject, Suspend, Reinstate, ChangeRequestApprove/Reject). Null for
    /// user-driven transitions; see <see cref="ActorUserId"/>.
    /// </summary>
    public string? ActorAdminId { get; private set; }

    /// <summary>Free-form reason text — populated for Rejected / Suspended / ChangeRequestRejected.</summary>
    public string? Reason { get; private set; }

    /// <summary>
    /// JSON snapshot of the relevant fields at the moment of the action.
    /// Shape varies per <see cref="Action"/>. Stored as jsonb so future
    /// queries can index into it without a schema change.
    /// </summary>
    public string? SnapshotJson { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    private EstablishmentReviewHistory() { }

    public EstablishmentReviewHistory(
        Guid id,
        Guid establishmentId,
        EstablishmentReviewAction action,
        DateTimeOffset occurredAt,
        Guid? changeRequestId = null,
        string? actorUserId = null,
        string? actorAdminId = null,
        string? reason = null,
        string? snapshotJson = null)
    {
        Id = id;
        EstablishmentId = establishmentId;
        Action = action;
        OccurredAt = occurredAt;
        ChangeRequestId = changeRequestId;
        ActorUserId = actorUserId;
        ActorAdminId = actorAdminId;
        Reason = reason;
        SnapshotJson = snapshotJson;
    }
}
