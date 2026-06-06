using System.Text.Json.Serialization;

namespace Matloob.Domain.Establishments;

/// <summary>
/// Lifecycle state of an <see cref="EstablishmentInvitation"/>.
///
/// <list type="bullet">
///   <item><see cref="Pending"/> — issued, not yet accepted/revoked/expired.
///     At most one Pending invite per (establishment, email).</item>
///   <item><see cref="Accepted"/> — the invitee accepted. The membership row
///     may be created immediately (invitee already had a local users row) or
///     deferred to the next authenticated request (deferred materialization
///     via the CurrentUserSyncService sweep).</item>
///   <item><see cref="Revoked"/> — the Owner cancelled the invite.</item>
///   <item><see cref="Expired"/> — past <c>expires_at</c> without acceptance.</item>
/// </list>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EstablishmentInvitationStatus>))]
public enum EstablishmentInvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Revoked = 2,
    Expired = 3,
}
