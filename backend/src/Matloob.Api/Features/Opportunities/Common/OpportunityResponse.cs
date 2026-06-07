using System.Text.Json.Serialization;

namespace Matloob.Api.Features.Opportunities.Common;

/// <summary>
/// Wire shape returned by every opportunity read endpoint. Mirrors the
/// Laravel <c>Establishments/Opportunities/OpportunityResource</c>
/// field-for-field (snake_case keys via
/// <see cref="JsonPropertyNameAttribute"/>) with one structural change:
/// the <c>contracts_count</c> field is DROPPED because Contracts are no
/// longer a concept in the new system
/// (see docs/25-ajeer-disposition.md).
///
/// <para>
/// Fields whose backing data isn't migrated yet are emitted as null/[]/0
/// placeholders so the public frontend's parser keeps working unchanged.
/// </para>
/// </summary>
public sealed class OpportunityResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("start_date")]
    public string StartDate { get; init; } = string.Empty;

    [JsonPropertyName("end_date")]
    public string EndDate { get; init; } = string.Empty;

    [JsonPropertyName("lat")]
    public decimal Lat { get; init; }

    [JsonPropertyName("lon")]
    public decimal Lon { get; init; }

    [JsonPropertyName("location_title")]
    public string LocationTitle { get; init; } = string.Empty;

    [JsonPropertyName("required_personnel")]
    public int RequiredPersonnel { get; init; }

    [JsonPropertyName("monthly_salary")]
    public decimal? MonthlySalary { get; init; }

    [JsonPropertyName("years_of_experience_required")]
    public byte? YearsOfExperienceRequired { get; init; }

    /// <summary>
    /// Legacy MySQL stored this as a CSV column; the Opportunity model
    /// exposed an accessor that returned an array via
    /// <c>explode(',', ...)</c>. We emit the array directly.
    /// </summary>
    [JsonPropertyName("establishment_classification")]
    public IReadOnlyList<string> EstablishmentClassification { get; init; } = [];

    /// <summary>
    /// Localized concatenated label list (placeholder; future i18n hook).
    /// </summary>
    [JsonPropertyName("establishment_classification_label")]
    public string? EstablishmentClassificationLabel { get; init; }

    [JsonPropertyName("working_hours_type")]
    public string? WorkingHoursType { get; init; }

    [JsonPropertyName("working_hours_type_label")]
    public string? WorkingHoursTypeLabel { get; init; }

    [JsonPropertyName("working_hours_from")]
    public string? WorkingHoursFrom { get; init; }

    [JsonPropertyName("working_hours_to")]
    public string? WorkingHoursTo { get; init; }

    [JsonPropertyName("fees")]
    public decimal? Fees { get; init; }

    [JsonPropertyName("phone_contact_information")]
    public string? PhoneContactInformation { get; init; }

    [JsonPropertyName("email_contact_information")]
    public string? EmailContactInformation { get; init; }

    [JsonPropertyName("gender")]
    public IReadOnlyList<string> Gender { get; init; } = [];

    [JsonPropertyName("gender_label")]
    public string? GenderLabel { get; init; }

    /// <summary>
    /// Nationality minimal projection (id + name) or null when not
    /// loaded / not set on the opportunity.
    /// </summary>
    [JsonPropertyName("nationality")]
    public NamedRefDto? Nationality { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>Placeholder — Laravel emits a card-type code per status.</summary>
    [JsonPropertyName("card_type")]
    public string? CardType { get; init; }

    /// <summary>Placeholder — Laravel emits a localized status label.</summary>
    [JsonPropertyName("status_label")]
    public string? StatusLabel { get; init; }

    /// <summary>Placeholder — Laravel emits a status icon code.</summary>
    [JsonPropertyName("status_icon")]
    public string? StatusIcon { get; init; }

    /// <summary>
    /// The owning event (minimal projection). The public frontend's
    /// opportunity cards dereference <c>opportunity.event.name</c>
    /// unconditionally, so this must be non-null on any list/show the UI
    /// renders. Populated by the read endpoints that load the event; null
    /// only where the event isn't hydrated.
    /// </summary>
    [JsonPropertyName("event")]
    public OpportunityEventDto? Event { get; init; }

    [JsonPropertyName("opportunity_category")]
    public OpportunityCategoryDto? OpportunityCategory { get; init; }

    /// <summary>
    /// Uploaded file metadata for the opportunity's media collection.
    /// Replaces Laravel's Spatie media URLs with Asset GUIDs + minimal
    /// file metadata.
    /// </summary>
    [JsonPropertyName("uploads")]
    public IReadOnlyList<OpportunityUploadDto> Uploads { get; init; } = [];

    /// <summary>
    /// Inline applications array. Only populated on the detail endpoint;
    /// the list endpoints return an empty array (Laravel's `whenLoaded`
    /// pattern — caller never relies on shape difference).
    /// </summary>
    [JsonPropertyName("applicants")]
    public IReadOnlyList<object> Applicants { get; init; } = [];

    [JsonPropertyName("applicants_count")]
    public int ApplicantsCount { get; init; }

    /// <summary>Filled positions = active (Accepted/cancellation-pending) offers
    /// on the opportunity. An accepted offer is the contract in the new model,
    /// so this restores the legacy <c>contracts_count</c> the cards use to
    /// compute "vacancies left" = required_personnel − contracts_count.</summary>
    [JsonPropertyName("contracts_count")]
    public int ContractsCount { get; init; }

    [JsonPropertyName("can_end")]
    public bool CanEnd { get; init; }

    /// <summary>
    /// Minimal issuer projection. Full Laravel
    /// <c>EstablishmentResource</c> shape will land when the
    /// establishment composite read shape stabilises.
    /// </summary>
    [JsonPropertyName("issuer")]
    public OpportunityIssuerDto? Issuer { get; init; }

    [JsonPropertyName("success_criteria")]
    public IReadOnlyList<SuccessCriterionDto> SuccessCriteria { get; init; } = [];

    [JsonPropertyName("is_applied")]
    public bool? IsApplied { get; init; }
}

/// <summary>
/// Minimal projection of the opportunity's owning event. Mirrors the
/// fields the public frontend's <c>Event</c> type exposes that the
/// opportunity cards actually read (<c>name</c>, <c>lat</c>, <c>lon</c>);
/// the rest are emitted for parity with the Laravel <c>EventResource</c>
/// nested under each opportunity.
/// </summary>
public sealed class OpportunityEventDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("start_date")]
    public string? StartDate { get; init; }

    [JsonPropertyName("end_date")]
    public string? EndDate { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("lat")]
    public string? Lat { get; init; }

    [JsonPropertyName("lon")]
    public string? Lon { get; init; }

    [JsonPropertyName("location_title")]
    public string? LocationTitle { get; init; }
}

/// <summary>
/// Minimal id+name reference used for nationality, region, city, etc.
/// </summary>
public sealed class NamedRefDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed class OpportunityCategoryDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    [JsonPropertyName("for_vacancy")]
    public bool ForVacancy { get; init; }

    [JsonPropertyName("is_other")]
    public bool IsOther { get; init; }

    /// <summary>
    /// Hydrated parent category for child rows. Categories are a 2-level tree;
    /// children carry the user-facing title (e.g. استقبال) while the parent
    /// holds the icon (e.g. assets/icons/opportunity_categories/temporary_work.svg).
    /// The frontend opportunity cards read <c>opportunity_category.parent?.icon
    /// || opportunity_category.icon</c>, so without this they render the
    /// fallback bx:party icon for every child-categorized opportunity.
    /// Null for root-level categories (or when the parent row no longer
    /// exists).
    /// </summary>
    [JsonPropertyName("parent")]
    public OpportunityCategoryDto? Parent { get; init; }
}

public sealed class OpportunityUploadDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("file_name")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("content_type")]
    public string ContentType { get; init; } = string.Empty;

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; init; }
}

public sealed class OpportunityIssuerDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("logo")]
    public string? Logo { get; init; }
}

public sealed class SuccessCriterionDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("output")]
    public string Output { get; init; } = string.Empty;

    [JsonPropertyName("success_criteria")]
    public string SuccessCriteria { get; init; } = string.Empty;

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("uploads")]
    public IReadOnlyList<OpportunityUploadDto> Uploads { get; init; } = [];
}
