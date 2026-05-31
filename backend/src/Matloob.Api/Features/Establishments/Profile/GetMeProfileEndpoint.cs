using System.Text.Json.Serialization;
using FastEndpoints;
using Matloob.Api.Features.Establishments.Common;
using Matloob.Api.Infrastructure.Identity;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Domain.Establishments;
using Microsoft.EntityFrameworkCore;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Matloob.Api.Features.Establishments.Profile;

/// <summary>
/// <c>GET /api/establishments/me/profile</c> (Laravel-compat) +
/// <c>GET /api/v1/establishments/me/profile</c> (canonical) — returns a
/// composite establishment profile shaped after Laravel
/// <c>Establishments/Auth/EstablishmentResource</c> +
/// <c>Establishments/Me/Profile/ProfileResource</c>.
///
/// <para>
/// Establishment resolution. Laravel relied on the
/// <c>X-Commissioner-UUID</c> header + <c>establishment.context</c>
/// middleware to pick "which establishment is me." The new API removed
/// that header (see compatibility matrix §<c>Establishment-side API</c>);
/// callers identify the establishment via, in order:
/// </para>
/// <list type="number">
/// <item>query parameter <c>?establishment_id={guid}</c>, or</item>
/// <item>header <c>X-Establishment-Id: {guid}</c>, or</item>
/// <item>auto-resolution: if the caller has exactly ONE active membership
///   in an Approved/Suspended establishment, that one is used.</item>
/// </list>
/// <para>
/// If the caller has &gt;1 active membership and didn't disambiguate, this
/// endpoint returns 400 with code <c>establishment_context_required</c>
/// so the public frontend can render an establishment picker.
/// </para>
///
/// <para>
/// Response shape. Mirrors Laravel field-for-field. Fields whose backing
/// concept is not migrated yet land as null/[]/0 placeholders
/// (services, products, participations, evaluations, reviews,
/// experiences, bank_account, logo, rate, total_reviews,
/// profile_complete_percentage). Each placeholder is replaced as the
/// matching feature lands; see <c>docs/40-api-migration-readiness.md</c>.
/// </para>
///
/// <para>
/// Authorization. The caller must be an active member of the resolved
/// establishment, OR <c>matloob_admin</c>. Otherwise 404 (per the same
/// enumeration-leak policy as the canonical details endpoint).
/// </para>
/// </summary>
public sealed class GetMeProfileEndpoint
    : EndpointWithoutRequest<EstablishmentMeProfileResponse>
{
    private const string EstablishmentIdQueryKey = "establishment_id";
    private const string EstablishmentIdHeader = "X-Establishment-Id";

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetMeProfileEndpoint(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public override void Configure()
    {
        Get("/api/establishments/me/profile", "/api/v1/establishments/me/profile");
        Description(b => b
            .Produces<EstablishmentMeProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Establishments"));
        Summary(s =>
        {
            s.Summary = "Establishment profile for the current member (Laravel-shape).";
            s.Description =
                "Resolves the establishment from ?establishment_id, " +
                "X-Establishment-Id header, or the caller's single active " +
                "membership. 400 if ambiguous; 404 if no membership.";
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

        // Use the shared resolver so the X-Commissioner-UUID alias and
        // the canonical X-Establishment-Id / ?establishment_id chain
        // stay in sync across every legacy endpoint.
        var result = await EstablishmentContextResolver.ResolveAsync(
            HttpContext, _db, sub, ct);

        Guid resolvedId;
        switch (result.Outcome)
        {
            case EstablishmentContextOutcome.Resolved:
                resolvedId = result.EstablishmentId;
                break;
            case EstablishmentContextOutcome.NotFound:
                await Send.NotFoundAsync(ct);
                return;
            case EstablishmentContextOutcome.Ambiguous:
                await WriteProblemAsync(
                    StatusCodes.Status400BadRequest,
                    "establishment_context_required",
                    "The caller is a member of multiple establishments. Specify ?establishment_id={guid} or the X-Establishment-Id header.",
                    ct);
                return;
            default:
                await Send.NotFoundAsync(ct);
                return;
        }

        var establishment = await _db.Establishments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == resolvedId, ct);
        if (establishment is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var isAdmin = MembershipChecks.IsAdmin(HttpContext.User);
        var isMember = await _db.EstablishmentMembers
            .AsNoTracking()
            .AnyAsync(m => m.EstablishmentId == resolvedId
                       && m.UserId == sub
                       && m.IsActive, ct);
        if (!isAdmin && !isMember)
        {
            // 404 not 403 — same enumeration-leak policy as the canonical
            // details endpoint.
            await Send.NotFoundAsync(ct);
            return;
        }

        var response = await EstablishmentProfileReadMapper.BuildAsync(_db, establishment, ct);
        await Send.OkAsync(response, ct);
    }

    private async Task WriteProblemAsync(int status, string code, string detail, CancellationToken ct)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status switch
            {
                StatusCodes.Status400BadRequest => "Bad Request",
                _ => "Error",
            },
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}",
        };
        problem.Extensions["code"] = code;
        HttpContext.Response.StatusCode = status;
        HttpContext.Response.ContentType = "application/problem+json";
        await HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken: ct);
    }
}

// -- Response shape ----------------------------------------------------------

public sealed class EstablishmentMeProfileResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("profile_complete_percentage")]
    public int ProfileCompletePercentage { get; init; }

    [JsonPropertyName("logo")]
    public string? Logo { get; init; }

    [JsonPropertyName("profile")]
    public EstablishmentProfileBlock Profile { get; init; } = new();

    [JsonPropertyName("rate")]
    public double? Rate { get; init; }

    [JsonPropertyName("total_reviews")]
    public int? TotalReviews { get; init; }

    [JsonPropertyName("can_manage_events")]
    public bool CanManageEvents { get; init; }
}

public sealed class EstablishmentProfileBlock
{
    [JsonPropertyName("general_info")]
    public EstablishmentGeneralInfoBlock GeneralInfo { get; init; } = new();

    [JsonPropertyName("contact_info")]
    public EstablishmentContactInfoBlock ContactInfo { get; init; } = new();

    [JsonPropertyName("services")]
    public IReadOnlyList<object> Services { get; init; } = [];

    [JsonPropertyName("products")]
    public IReadOnlyList<object> Products { get; init; } = [];

    [JsonPropertyName("bank_account")]
    public object? BankAccount { get; init; }

    [JsonPropertyName("participations")]
    public IReadOnlyList<object> Participations { get; init; } = [];

    [JsonPropertyName("evaluations")]
    public IReadOnlyList<object> Evaluations { get; init; } = [];

    [JsonPropertyName("reviews")]
    public IReadOnlyList<object> Reviews { get; init; } = [];

    [JsonPropertyName("experiences")]
    public IReadOnlyList<object> Experiences { get; init; } = [];
}

public sealed class EstablishmentGeneralInfoBlock
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("establishment_status")]
    public string EstablishmentStatus { get; init; } = string.Empty;

    [JsonPropertyName("economic_activity")]
    public string? EconomicActivity { get; init; }

    [JsonPropertyName("sub_economic_activity")]
    public string? SubEconomicActivity { get; init; }

    [JsonPropertyName("cr_number")]
    public string CrNumber { get; init; } = string.Empty;

    [JsonPropertyName("establishment_size")]
    public string? EstablishmentSize { get; init; }

    [JsonPropertyName("cr_number_expiry")]
    public string? CrNumberExpiry { get; init; }

    [JsonPropertyName("years_of_experience")]
    public int? YearsOfExperience { get; init; }

    [JsonPropertyName("area")]
    public string? Area { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("lat")]
    public decimal? Lat { get; init; }

    [JsonPropertyName("lon")]
    public decimal? Lon { get; init; }

    [JsonPropertyName("location_title")]
    public string? LocationTitle { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("neighborhood")]
    public string? Neighborhood { get; init; }

    [JsonPropertyName("street_name")]
    public string? StreetName { get; init; }

    [JsonPropertyName("building_number")]
    public string? BuildingNumber { get; init; }

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; init; }

    [JsonPropertyName("additional_number")]
    public string? AdditionalNumber { get; init; }

    [JsonPropertyName("website")]
    public string? Website { get; init; }
}

public sealed class EstablishmentContactInfoBlock
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("contact_number")]
    public string? ContactNumber { get; init; }

    [JsonPropertyName("additional_contact_number")]
    public string? AdditionalContactNumber { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

/// <summary>
/// <c>bank_account</c> block — mirrors the legacy <c>BankAccountResource</c>
/// (id + name + iban + nested bank ref), matching the frontend's
/// <c>BankAccount</c> type.
/// </summary>
public sealed record EstablishmentBankAccountBlock(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("iban")] string Iban,
    [property: JsonPropertyName("bank")] EstablishmentBankRef Bank);

public sealed record EstablishmentBankRef(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name);
