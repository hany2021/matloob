using System.Text.Json.Serialization;
using FastEndpoints;
using Matloob.Api.Features.Common;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Profile.Show;

/// <summary>
/// <c>GET /api/v1/profile</c> (canonical) and <c>GET /api/users/profile</c>
/// (Laravel-compat alias) — return the current user's profile.
///
/// Response is wrapped in the Laravel <c>{ "data": { ... } }</c> envelope
/// (<see cref="DataEnvelope{T}"/>) because the public frontend's profile hook
/// reads <c>response.data.data</c>. The inner object mirrors Laravel
/// <c>UserResource::toArray()</c> field-for-field (snake_case keys).
///
/// Relations are projected from the migrated profile tables (education,
/// experiences, certificates, skills, languages, professions, supportive
/// documents, bank account) and the reference lookups (city/region/
/// nationality/bank). Identity-sourced fields (id_number, gender, age, dob,
/// nationality) surface once the sync layer backfills them.
///
/// Auth: any authenticated principal. Anonymous → 401. The
/// <c>CurrentUserSyncMiddleware</c> has already created or refreshed the
/// local row by the time this endpoint runs.
/// </summary>
public sealed class GetProfileEndpoint : EndpointWithoutRequest<DataEnvelope<ProfileResponse>>
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetProfileEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get("/api/v1/profile", "/api/users/profile");
        Description(b => b
            .Produces<DataEnvelope<ProfileResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("Profile"));
        Summary(s =>
        {
            s.Summary = "Current user's profile (Laravel UserResource shape).";
            s.Description =
                "Authenticated. Snake_case shape matches Laravel UserResource, " +
                "wrapped in a { data } envelope.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var sub = _currentUser.UserId;
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.IdentityId == sub, ct);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var response = await ProfileReadMapper.BuildAsync(_db, user, ct);
        await Send.OkAsync(new DataEnvelope<ProfileResponse>(response), ct);
    }
}

// ---- Nested resource DTOs (snake_case, mirroring the Laravel sub-resources) --

public sealed record RefDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record AssetRefDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("url")] string Url);

public sealed record BankAccountDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("bank")] RefDto Bank,
    [property: JsonPropertyName("iban")] string Iban);

public sealed record LanguageDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("level_label")] string LevelLabel);

public sealed record SkillDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("level")] string Level);

public sealed record EducationDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("degree")] string Degree,
    [property: JsonPropertyName("degree_label")] string DegreeLabel,
    [property: JsonPropertyName("specialization")] string? Specialization,
    [property: JsonPropertyName("gpa_system")] int GpaSystem,
    [property: JsonPropertyName("gpa")] decimal Gpa,
    [property: JsonPropertyName("graduation_year")] int GraduationYear,
    [property: JsonPropertyName("copy")] AssetRefDto? Copy);

public sealed record ExperienceDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("company")] string Company,
    [property: JsonPropertyName("position")] string Position,
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] string? To,
    [property: JsonPropertyName("current")] bool Current,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("type_label")] string TypeLabel);

public sealed record CertificateDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("issued_by")] string? IssuedBy,
    [property: JsonPropertyName("issued_at")] string? IssuedAt,
    [property: JsonPropertyName("copy")] AssetRefDto? Copy);

public sealed record ProfessionDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("for_vacancy")] bool ForVacancy,
    [property: JsonPropertyName("is_other")] bool IsOther,
    [property: JsonPropertyName("other")] string? Other);

public sealed record SupportiveDocumentDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("file")] AssetRefDto? File,
    [property: JsonPropertyName("url")] string? Url);

/// <summary>
/// Inner object of <c>GET /api/v1/profile</c> (wrapped in
/// <see cref="DataEnvelope{T}"/>). Property names use snake_case via
/// <see cref="JsonPropertyNameAttribute"/> — a 1:1 mirror of Laravel
/// <c>UserResource::toArray()</c>, plus an <c>identity_id</c> extension.
/// </summary>
public sealed class ProfileResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("id_number")]
    public string? IdNumber { get; init; }

    [JsonPropertyName("gender")]
    public string? Gender { get; init; }

    [JsonPropertyName("nationality")]
    public object? Nationality { get; init; }

    [JsonPropertyName("age")]
    public int? Age { get; init; }

    [JsonPropertyName("date_of_birth")]
    public string? DateOfBirth { get; init; }

    [JsonPropertyName("hijri_date_of_birth")]
    public string? HijriDateOfBirth { get; init; }

    [JsonPropertyName("bio")]
    public string? Bio { get; init; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; init; }

    [JsonPropertyName("additional_phone_number")]
    public string? AdditionalPhoneNumber { get; init; }

    [JsonPropertyName("years_of_experience")]
    public int? YearsOfExperience { get; init; }

    [JsonPropertyName("passport_copy")]
    public string? PassportCopy { get; init; }

    [JsonPropertyName("photo")]
    public string? Photo { get; init; }

    [JsonPropertyName("professions")]
    public IReadOnlyList<object> Professions { get; init; } = [];

    [JsonPropertyName("experiences")]
    public IReadOnlyList<object> Experiences { get; init; } = [];

    [JsonPropertyName("certificates")]
    public IReadOnlyList<object> Certificates { get; init; } = [];

    [JsonPropertyName("skills")]
    public IReadOnlyList<object> Skills { get; init; } = [];

    [JsonPropertyName("education")]
    public IReadOnlyList<object> Education { get; init; } = [];

    [JsonPropertyName("city")]
    public object? City { get; init; }

    [JsonPropertyName("region")]
    public object? Region { get; init; }

    [JsonPropertyName("bank_account")]
    public object? BankAccount { get; init; }

    [JsonPropertyName("languages")]
    public IReadOnlyList<object> Languages { get; init; } = [];

    [JsonPropertyName("supportive_documents")]
    public IReadOnlyList<object> SupportiveDocuments { get; init; } = [];

    [JsonPropertyName("participations")]
    public IReadOnlyList<object> Participations { get; init; } = [];

    [JsonPropertyName("profile_complete_percentage")]
    public int ProfileCompletePercentage { get; init; }

    [JsonPropertyName("evaluations")]
    public IReadOnlyList<object> Evaluations { get; init; } = [];

    [JsonPropertyName("reviews")]
    public IReadOnlyList<object> Reviews { get; init; } = [];

    [JsonPropertyName("rate")]
    public double? Rate { get; init; }

    [JsonPropertyName("total_reviews")]
    public int? TotalReviews { get; init; }

    [JsonPropertyName("onboarded")]
    public bool Onboarded { get; init; }

    [JsonPropertyName("uncompleted_profile_sections")]
    public IReadOnlyList<string> UncompletedProfileSections { get; init; } = [];

    /// <summary>
    /// New-client extension: the IdM <c>sub</c> claim. Laravel parsers ignore
    /// unknown keys, so this is safe alongside the legacy field set.
    /// </summary>
    [JsonPropertyName("identity_id")]
    public string IdentityId { get; init; } = string.Empty;
}
