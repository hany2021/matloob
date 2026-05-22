using System.Text.Json.Serialization;
using FastEndpoints;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Matloob.Api.Features.Profile.Show;

/// <summary>
/// <c>GET /api/v1/profile</c> (canonical) and <c>GET /api/users/profile</c>
/// (Laravel-compat alias) — return the current user's profile.
///
/// Response shape mirrors Laravel <c>UserResource::toArray()</c> field-for-
/// field (snake_case keys). The local <c>users</c> table only carries
/// (id, name, email, phone) today, so all other Laravel relations land as
/// <c>null</c> or <c>[]</c> placeholders. Each placeholder is replaced as
/// the underlying feature is migrated (personal-info, education, skills,
/// experiences, certificates, photo). See
/// <c>docs/40-api-migration-readiness.md §6</c> for the open product
/// decisions blocking the matching PATCH endpoints.
///
/// Auth: any authenticated principal. Anonymous → 401. The
/// <c>CurrentUserSyncMiddleware</c> has already created or refreshed the
/// local row by the time this endpoint runs; if that failed we surface
/// 401 (the principal can retry).
/// </summary>
public sealed class GetProfileEndpoint : EndpointWithoutRequest<ProfileResponse>
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
        // Two routes for back-compat: the legacy /api/users/profile that
        // the Laravel public frontend already calls, and the new
        // /api/v1/profile that future clients should use.
        Get("/api/v1/profile", "/api/users/profile");
        Description(b => b
            .Produces<ProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("Profile"));
        Summary(s =>
        {
            s.Summary = "Current user's profile (Laravel UserResource shape).";
            s.Description =
                "Authenticated. Snake_case shape matches Laravel UserResource. " +
                "Relations not yet migrated land as null/[] placeholders.";
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

        var response = new ProfileResponse
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            PhoneNumber = user.Phone,
            IdentityId = user.IdentityId,

            // Laravel-compat placeholders. Each lands when its feature
            // is migrated (docs/40-api-migration-readiness.md §6/§7).
            IdNumber = null,
            Gender = null,
            Nationality = null,
            Age = null,
            DateOfBirth = null,
            HijriDateOfBirth = null,
            Bio = null,
            AdditionalPhoneNumber = null,
            YearsOfExperience = null,
            PassportCopy = null,
            Photo = null,
            Professions = [],
            Experiences = [],
            Certificates = [],
            Skills = [],
            Education = [],
            City = null,
            Region = null,
            BankAccount = null,
            Languages = [],
            SupportiveDocuments = [],
            Participations = [],
            ProfileCompletePercentage = 0,
            Evaluations = [],
            Reviews = [],
            Rate = null,
            TotalReviews = null,
            Onboarded = false,
            UncompletedProfileSections = ["personal-info", "education-info", "interests-info", "experiences-info"],
        };

        await Send.OkAsync(response, ct);
    }
}

/// <summary>
/// Wire shape returned by <c>GET /api/v1/profile</c> + <c>GET /api/users/profile</c>.
/// Property names use snake_case via <see cref="JsonPropertyNameAttribute"/>
/// so the public frontend's Laravel-era parser keeps working unchanged.
///
/// Field set is a 1:1 mirror of Laravel <c>UserResource::toArray()</c>,
/// plus an <c>identity_id</c> extension (sub claim) that new clients may
/// consume but Laravel parsers ignore.
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
    /// New-client extension: the IdM <c>sub</c> claim that uniquely
    /// identifies the user. Laravel parsers ignore unknown keys, so this
    /// is safe to surface alongside the legacy field set.
    /// </summary>
    [JsonPropertyName("identity_id")]
    public string IdentityId { get; init; } = string.Empty;
}
