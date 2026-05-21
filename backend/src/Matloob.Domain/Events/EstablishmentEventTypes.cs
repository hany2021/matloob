namespace Matloob.Domain.Events;

/// <summary>
/// Canonical event-name strings the establishment endpoints emit. Used
/// both by the writer (when inserting outbox rows) and by future
/// subscribers branching on <see cref="OutboxEvent.EventType"/>. Keep in
/// sync with docs/15-establishment-onboarding-spec.md §11.
/// </summary>
public static class EstablishmentEventTypes
{
    public const string SubmittedForReview = "EstablishmentSubmittedForReview";
    public const string Approved = "EstablishmentApproved";
    public const string Rejected = "EstablishmentRejected";
    public const string Suspended = "EstablishmentSuspended";
    public const string Reinstated = "EstablishmentReinstated";

    public const string MemberAdded = "EstablishmentMemberAdded";
    public const string MemberRemoved = "EstablishmentMemberRemoved";

    public const string ChangeRequestSubmitted = "EstablishmentChangeRequestSubmitted";
    public const string ChangeRequestApproved = "EstablishmentChangeRequestApproved";
    public const string ChangeRequestRejected = "EstablishmentChangeRequestRejected";
    public const string ChangeRequestCancelled = "EstablishmentChangeRequestCancelled";
}
