using Matloob.Domain.Common;

namespace Matloob.Domain.Establishments;

/// <summary>
/// An invitation for someone (identified by email) to join an
/// <see cref="Establishment"/> with a given role. Mirrors the shape of
/// <see cref="EstablishmentMember"/> (sealed, private setters, behavioral
/// methods) and is the aggregate behind the invite-by-email flow.
///
/// The raw token is never persisted: only its SHA-256 hex digest
/// (<see cref="TokenHash"/>) is stored. The raw token lives only inside the
/// create/resend request and is composed into the invite URL e-mailed out.
///
/// Materialization: on accept we may create the <see cref="EstablishmentMember"/>
/// row immediately (the invitee already had a local users row) or defer it to
/// the next authenticated request (the CurrentUserSyncService sweep keys off
/// <see cref="MaterializedMemberId"/> being null for an Accepted invite).
/// </summary>
public sealed class EstablishmentInvitation : BaseAuditableEntity<Guid>
{
    public Guid EstablishmentId { get; private set; }

    /// <summary>Normalized (lower-cased, trimmed) invitee email.</summary>
    public string Email { get; private set; } = string.Empty;

    public EstablishmentMemberRole Role { get; private set; } = EstablishmentMemberRole.Other;

    /// <summary>SHA-256 hex digest of the raw token. The raw token is never stored.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public EstablishmentInvitationStatus Status { get; private set; } =
        EstablishmentInvitationStatus.Pending;

    /// <summary>Sub claim of the Owner who issued the invite.</summary>
    public string InvitedByUserId { get; private set; } = string.Empty;

    public DateTimeOffset InvitedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    /// <summary>Sub claim of whoever accepted (set at accept time).</summary>
    public string? AcceptedByUserId { get; private set; }

    /// <summary>Soft FK to the materialized <c>establishment_members</c> row.</summary>
    public Guid? MaterializedMemberId { get; private set; }

    private EstablishmentInvitation() { }

    public EstablishmentInvitation(
        Guid id,
        Guid establishmentId,
        string email,
        EstablishmentMemberRole role,
        string tokenHash,
        string invitedByUserId,
        DateTimeOffset invitedAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        EstablishmentId = establishmentId;
        Email = Normalize(email);
        Role = role;
        TokenHash = tokenHash;
        InvitedByUserId = invitedByUserId;
        InvitedAt = invitedAt;
        ExpiresAt = expiresAt;
        Status = EstablishmentInvitationStatus.Pending;
    }

    /// <summary>Normalize an email for storage / comparison: trim + lower-case.</summary>
    public static string Normalize(string email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Owner cancels a still-pending invite.</summary>
    public void Revoke()
    {
        Status = EstablishmentInvitationStatus.Revoked;
    }

    /// <summary>
    /// Re-issue a pending invite with a fresh token + expiry (new email fired
    /// by the caller). Only meaningful while Pending.
    /// </summary>
    public void Resend(string newTokenHash, DateTimeOffset newInvitedAt, DateTimeOffset newExpiresAt)
    {
        TokenHash = newTokenHash;
        InvitedAt = newInvitedAt;
        ExpiresAt = newExpiresAt;
        Status = EstablishmentInvitationStatus.Pending;
    }

    /// <summary>
    /// Mark accepted by <paramref name="acceptedByUserId"/>. Membership
    /// materialization is recorded separately via <see cref="SetMaterializedMember"/>.
    /// </summary>
    public void Accept(DateTimeOffset acceptedAt, string acceptedByUserId)
    {
        Status = EstablishmentInvitationStatus.Accepted;
        AcceptedAt = acceptedAt;
        AcceptedByUserId = acceptedByUserId;
    }

    /// <summary>Bind the invite to the membership row it produced.</summary>
    public void SetMaterializedMember(Guid memberId)
    {
        MaterializedMemberId = memberId;
    }

    public void MarkExpired()
    {
        Status = EstablishmentInvitationStatus.Expired;
    }

    /// <summary>True when the invite can still be previewed / accepted.</summary>
    public bool IsAcceptable(DateTimeOffset now) =>
        Status == EstablishmentInvitationStatus.Pending && ExpiresAt > now;
}
