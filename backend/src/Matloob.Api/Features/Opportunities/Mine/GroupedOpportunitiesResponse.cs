using System.Text.Json.Serialization;
using Matloob.Api.Features.Opportunities.Common;

namespace Matloob.Api.Features.Opportunities.Mine;

/// <summary>
/// The owner "My Opportunities" list, grouped by status into the legacy
/// double-nested shape the public frontend reads as
/// <c>data[status][status].{ status_label, status_icon, card_type, data }</c>
/// (see <c>OrganizerOpportunities.tsx</c>). Mirrors the Laravel
/// <c>GroupedOpportunityResource</c> + <c>OpportunitySupport::getOpportunitiesGroupedByStatus</c>,
/// and is structurally identical to the events list's
/// <c>GroupedEventsResponse</c>.
///
/// <para>
/// NOT <c>IBypassEnvelope</c>: the global response shim adds the outer
/// <c>{ data }</c> wrapper, leaving the four status keys directly under it.
/// </para>
/// </summary>
public sealed class GroupedOpportunitiesResponse
{
    [JsonPropertyName("active")]
    public Dictionary<string, OpportunityGroup> Active { get; init; } = new();

    [JsonPropertyName("upcoming")]
    public Dictionary<string, OpportunityGroup> Upcoming { get; init; } = new();

    [JsonPropertyName("drafted")]
    public Dictionary<string, OpportunityGroup> Drafted { get; init; } = new();

    [JsonPropertyName("ended")]
    public Dictionary<string, OpportunityGroup> Ended { get; init; } = new();
}

/// <summary>
/// One status bucket: the localized label/icon/card-type plus the
/// opportunities in it. Keyed under its own status token by the parent
/// (e.g. <c>{ "active": { "active": OpportunityGroup } }</c>).
/// </summary>
public sealed class OpportunityGroup
{
    [JsonPropertyName("status_label")]
    public string StatusLabel { get; init; } = string.Empty;

    [JsonPropertyName("status_icon")]
    public string StatusIcon { get; init; } = string.Empty;

    [JsonPropertyName("card_type")]
    public string CardType { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public IReadOnlyList<OpportunityResponse> Data { get; init; } = [];
}

/// <summary>
/// Minimal applicant projection emitted in each opportunity's
/// <c>applicants</c> array on the owner list. The public frontend's
/// opportunity card only reads <c>applicants.length</c> (the avatar strip
/// is commented out), so id + applicant identity is enough; the full
/// applicant detail comes from the dedicated applicants endpoint.
/// </summary>
public sealed class OpportunityApplicantRef
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("applicant_user_id")]
    public string? ApplicantUserId { get; init; }

    [JsonPropertyName("applicant_establishment_id")]
    public Guid? ApplicantEstablishmentId { get; init; }
}
