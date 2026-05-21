using Matloob.Domain.Common;

namespace Matloob.Domain.Establishments;

/// <summary>
/// Membership row binding a user to an <see cref="Establishment"/> with a
/// role. See docs/15-establishment-onboarding-spec.md §6.
///
/// Rules enforced via DB index + validators (not in this commit):
/// - Partial unique on <c>(establishment_id, user_id) WHERE is_deleted = false</c>
///   — one active membership per (establishment, user) pair.
/// - At least one active row with <c>Role = Owner</c> per establishment
///   must always exist (last-Owner protection).
/// - Deactivation (<see cref="IsActive"/>) is preferred over removal; both
///   are supported and both soft-delete on Remove() — soft-delete cascade
///   from the parent Establishment also clears member rows.
/// </summary>
public sealed class EstablishmentMember : BaseAuditableEntity<Guid>
{
    public Guid EstablishmentId { get; private set; }

    /// <summary>Sub claim of the member. Soft FK; Users table arrives in a later phase.</summary>
    public string UserId { get; private set; } = string.Empty;

    public EstablishmentMemberRole Role { get; private set; } = EstablishmentMemberRole.Other;
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Sub claim of the Owner who added this member. Audit-only. May be
    /// empty for the initial-Owner row that the system inserts on approval
    /// (see <see cref="System"/> sentinel; we just leave it blank there).
    /// </summary>
    public string AddedByUserId { get; private set; } = string.Empty;

    public DateTimeOffset AddedAt { get; private set; }

    private EstablishmentMember() { }

    public EstablishmentMember(
        Guid id,
        Guid establishmentId,
        string userId,
        EstablishmentMemberRole role,
        string addedByUserId,
        DateTimeOffset addedAt,
        bool isActive = true)
    {
        Id = id;
        EstablishmentId = establishmentId;
        UserId = userId;
        Role = role;
        AddedByUserId = addedByUserId;
        AddedAt = addedAt;
        IsActive = isActive;
    }
}
