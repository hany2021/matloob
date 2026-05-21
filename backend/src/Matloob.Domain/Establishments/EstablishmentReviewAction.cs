using System.Text.Json.Serialization;

namespace Matloob.Domain.Establishments;

/// <summary>
/// What happened, captured one row at a time in
/// <see cref="EstablishmentReviewHistory"/>. Append-only audit trail; values
/// are never deleted or rewritten.
///
/// Names match the events listed in
/// docs/15-establishment-onboarding-spec.md §11 plus the implicit Draft
/// creation step.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EstablishmentReviewAction>))]
public enum EstablishmentReviewAction
{
    Created = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Suspended = 4,
    Reinstated = 5,
    ChangeRequestSubmitted = 6,
    ChangeRequestApproved = 7,
    ChangeRequestRejected = 8,
    ChangeRequestCancelled = 9,
    MemberAdded = 10,
    MemberRemoved = 11,
    MemberRoleChanged = 12,
}
