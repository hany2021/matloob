using System.Text.Json.Serialization;

namespace Matloob.Domain.Establishments;

/// <summary>
/// Lifecycle states for an <see cref="Establishment"/>. See
/// docs/15-establishment-onboarding-spec.md §2.
///
/// Status drives:
/// - the unique-CR-number partial index (PendingReview / Approved / Suspended)
/// - the suspension lock (Suspended blocks mutations with 423 Locked)
/// - whether the row is user-editable vs. ChangeRequest-only.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EstablishmentStatus>))]
public enum EstablishmentStatus
{
    /// <summary>Owner is filling in the form. Editable. Not yet visible to admins.</summary>
    Draft = 0,

    /// <summary>Submitted; sitting in the admin review queue. Frozen to the user.</summary>
    PendingReview = 1,

    /// <summary>Active. Edits go through ChangeRequest. Operational.</summary>
    Approved = 2,

    /// <summary>Admin rejected. User can edit + resubmit (same row, transitions back to PendingReview).</summary>
    Rejected = 3,

    /// <summary>Admin halted operations. Reads work; writes return 423 Locked. Reinstatable.</summary>
    Suspended = 4,
}
