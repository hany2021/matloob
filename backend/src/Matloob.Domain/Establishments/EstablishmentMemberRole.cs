using System.Text.Json.Serialization;

namespace Matloob.Domain.Establishments;

/// <summary>
/// Role of an <see cref="EstablishmentMember"/> within their establishment.
/// See docs/15-establishment-onboarding-spec.md §6.1.
///
/// <see cref="Owner"/> is the only role with mutation rights today (adds
/// members, submits change requests, etc.). The other roles exist so the
/// org chart can be recorded; per-role permission grants land in a later
/// phase. Multiple Owners are allowed; at least one active Owner must
/// always exist (enforced in the AddMember / RemoveMember / UpdateRole
/// validators — last-Owner protection).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EstablishmentMemberRole>))]
public enum EstablishmentMemberRole
{
    Owner = 0,
    Manager = 1,
    HR = 2,
    Accountant = 3,
    Commissioner = 4,
    Other = 5,
}
