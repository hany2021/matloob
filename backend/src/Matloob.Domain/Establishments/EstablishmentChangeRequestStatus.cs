using System.Text.Json.Serialization;

namespace Matloob.Domain.Establishments;

/// <summary>
/// Lifecycle of an <see cref="EstablishmentChangeRequest"/>. See
/// docs/15-establishment-onboarding-spec.md §7.1.
///
/// Note: a partial unique index on
/// <c>(establishment_id) WHERE status = 'PendingReview' AND is_deleted = false</c>
/// enforces "one in-flight change request per establishment".
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EstablishmentChangeRequestStatus>))]
public enum EstablishmentChangeRequestStatus
{
    /// <summary>Owner is composing the proposed changes. Editable.</summary>
    Draft = 0,

    /// <summary>Submitted for admin review. Frozen.</summary>
    PendingReview = 1,

    /// <summary>Admin approved. Proposed values applied to Establishment; row stays as audit.</summary>
    Approved = 2,

    /// <summary>Admin rejected. Proposed changes discarded; Establishment unchanged.</summary>
    Rejected = 3,

    /// <summary>Submitter (or any active Owner) cancelled before review.</summary>
    Cancelled = 4,
}
