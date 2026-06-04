using System.Text.Json.Serialization;
using Matloob.Api.Features.Opportunities.Common;

namespace Matloob.Api.Features.Applications.Common;

/// <summary>
/// Wire shape returned by every application read endpoint. Mirrors the
/// legacy Laravel <c>OpportunityApplicationResource</c> snake_case field
/// set:
///
/// <list type="bullet">
///   <item><c>id</c> — application GUID.</item>
///   <item><c>applier_type</c> — <c>"user"</c> or <c>"organization"</c>
///     (Laravel returned <c>"organization"</c> for the establishment
///     morph alias; we keep that string for compat).</item>
///   <item><c>applier</c> — the full applying-party profile: the
///     individual <c>UserResource</c> (<c>user</c>) or the establishment
///     profile (<c>organization</c>), matching the frontend's
///     <c>IndividualProfile</c>/<c>EstablishmentProfile</c> types the
///     applicant detail page reads.</item>
///   <item><c>opportunity</c> — nested
///     <see cref="OpportunityResponse"/>.</item>
///   <item><c>status</c> + <c>status_label</c> — derived via
///     <see cref="ApplicationStatusComputer"/>.</item>
///   <item><c>created_at</c> — <c>yyyy-MM-dd</c>.</item>
///   <item><c>applied_by</c> — user who submitted the form for the
///     establishment applicant (null for individual applications).</item>
/// </list>
/// </summary>
public sealed class OpportunityApplicationResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("applier_type")]
    public string ApplierType { get; init; } = string.Empty;

    /// <summary>
    /// Full applier profile (the individual profile <c>ProfileResponse</c>
    /// or the establishment profile), or the minimal
    /// <see cref="ApplicationApplierDto"/> fallback. Typed <c>object</c>
    /// because the shape is polymorphic on <see cref="ApplierType"/>.
    /// </summary>
    [JsonPropertyName("applier")]
    public object? Applier { get; init; }

    [JsonPropertyName("opportunity")]
    public OpportunityResponse? Opportunity { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = ApplicationStatusComputer.Pending;

    [JsonPropertyName("status_label")]
    public string? StatusLabel { get; init; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("applied_by")]
    public ApplicationAppliedByDto? AppliedBy { get; init; }
}

/// <summary>
/// Minimal applier projection. For user applicants this is
/// <c>{id, name, email}</c> from the local users row; for establishment
/// applicants it is <c>{id, name, email}</c> from the establishments row.
/// Full Laravel resource hydration (nested profile, evaluations, etc.)
/// will land when those slices migrate.
/// </summary>
public sealed class ApplicationApplierDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

/// <summary>
/// "applied_by" is the human inside an establishment who submitted the
/// application — only present when <c>applier_type = "organization"</c>.
/// </summary>
public sealed class ApplicationAppliedByDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }
}
