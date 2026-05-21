using System.Globalization;
using System.Text.Json;
using FastEndpoints;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Matloob.Api.Features.Reference.InitData;

/// <summary>
/// <c>GET /api/v1/init-data</c> — composite bootstrap payload for the public
/// frontend. Field-for-field compatible with the legacy
/// <c>/api/init-data</c> Laravel endpoint (Phase-0 API matrix: <em>exact</em>).
///
/// Anonymous: the public frontend calls this before login, so no
/// authentication is required. The response is read-only reference data and
/// safe to expose.
///
/// Per-locale cached for 5 minutes via <see cref="IMemoryCache"/>. Cache key
/// includes the requested locale; a write to any reference table invalidates
/// implicitly when the entry expires.
/// </summary>
public sealed class GetInitDataEndpoint : EndpointWithoutRequest<InitDataResponse>
{
    private const string DefaultLocale = "en";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public GetInitDataEndpoint(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public override void Configure()
    {
        // Two routes for backwards-compat:
        //  - /api/v1/init-data: canonical (matches the new versioned scheme).
        //  - /api/init-data: legacy alias for the existing Angular public
        //    frontend, which hasn't been updated. Drop the alias once that SPA
        //    rolls onto the v1 base URL.
        // Two routes for backwards-compat. NOTE: do not set WithName(...) —
        // ASP.NET Core requires endpoint names to be globally unique, and the
        // alias would collide. Each route gets a framework-generated name.
        Get("/api/v1/init-data", "/api/init-data");
        AllowAnonymous();
        Description(b => b
            .Produces<InitDataResponse>(StatusCodes.Status200OK)
            .WithTags("Reference"));
        Summary(s =>
        {
            s.Summary = "Bootstrap payload: translations, settings, and every lookup table.";
            s.Description =
                "Anonymous. Cached server-side for 5 minutes per locale. " +
                "Optional ?locale=ar|en query selects the translations dictionary; defaults to 'en'.";
            s.Params["locale"] = "BCP-47 lowercase locale code. Defaults to 'en'.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var locale = (HttpContext.Request.Query["locale"].ToString() ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(locale))
        {
            locale = DefaultLocale;
        }
        locale = locale.ToLowerInvariant();

        var cacheKey = $"init-data::{locale}";
        var response = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await BuildResponseAsync(locale, ct);
        });

        await Send.OkAsync(response!, ct);
    }

    private async Task<InitDataResponse> BuildResponseAsync(string locale, CancellationToken ct)
    {
        // Pull each lookup with AsNoTracking — read-only DTO projection, never
        // tracked. EF translates the entire batch into 14 small SELECTs, each
        // hitting an is_active = true / soft-delete partial index.
        var cities = await _db.Cities
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new CityDto(x.Id, x.Name))
            .ToListAsync(ct);

        var regions = await _db.Regions
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new RegionDto(x.Id, x.Name))
            .ToListAsync(ct);

        var languages = await _db.Languages
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new LanguageDto(x.Id, x.Name))
            .ToListAsync(ct);

        var nationalities = await _db.Nationalities
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new NationalityDto(x.Id, x.Name))
            .ToListAsync(ct);

        var banks = await _db.Banks
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new BankDto(x.Id, x.Name))
            .ToListAsync(ct);

        var jobTitles = await _db.JobTitles
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new JobTitleDto(x.Id, x.Name))
            .ToListAsync(ct);

        var cancellationReasons = await _db.OfferCancellationReasons
            .AsNoTracking()
            .OrderBy(x => x.IsOther) // non-other first, "Other" last — matches legacy UX.
            .ThenBy(x => x.Name)
            .Select(x => new OfferCancellationReasonDto(x.Id, x.Name, x.IsOther))
            .ToListAsync(ct);

        var rejectionReasons = await _db.OfferRejectionReasons
            .AsNoTracking()
            .OrderBy(x => x.IsOther)
            .ThenBy(x => x.Name)
            .Select(x => new OfferRejectionReasonDto(x.Id, x.Name, x.IsOther))
            .ToListAsync(ct);

        var suggestedAttendees = await _db.SuggestedAttendees
            .AsNoTracking()
            .OrderBy(x => x.Min)
            .Select(x => new SuggestedAttendeeDto(x.Id, x.Min, x.Max))
            .ToListAsync(ct);

        var suggestedLocations = await _db.SuggestedLocations
            .AsNoTracking()
            .OrderBy(x => x.Title)
            .Select(x => new SuggestedLocationDto(x.Id, x.Title, x.Latitude, x.Longitude))
            .ToListAsync(ct);

        var eventTypes = await _db.EventTypes
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new EventTypeDto(x.Id, x.Name, x.Description, x.Background, x.Icon))
            .ToListAsync(ct);

        var seasons = await _db.Seasons
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SeasonDto(x.Id, x.Name, x.Image))
            .ToListAsync(ct);

        // Categories: pull all rows once, then build the three views client-side.
        // Avoids 3 round-trips and gives EF a chance to share the same query plan.
        var allCategories = await _db.OpportunityCategories
            .AsNoTracking()
            .OrderBy(x => x.Title)
            .Select(x => new
            {
                x.Id,
                x.ParentId,
                x.Title,
                x.Description,
                x.Icon,
                x.ForVacancy,
                x.IsOther,
            })
            .ToListAsync(ct);

        var opportunityCategories = allCategories
            .Select(x => new OpportunityCategoryDto(
                x.Id, x.Title, x.Description, x.Icon, x.ForVacancy, x.IsOther))
            .ToList();

        var childrenByParent = allCategories
            .Where(x => x.ParentId is not null)
            .GroupBy(x => x.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g
                .Select(c => new OpportunityCategoryDto(
                    c.Id, c.Title, c.Description, c.Icon, c.ForVacancy, c.IsOther))
                .ToList());

        var groupedCategories = allCategories
            .Where(x => x.ParentId is null)
            .Select(x => new OpportunityCategoryWithChildrenDto(
                x.Id, x.Title, x.Description, x.Icon, x.ForVacancy, x.IsOther,
                childrenByParent.TryGetValue(x.Id, out var kids)
                    ? kids
                    : new List<OpportunityCategoryDto>()))
            .ToList();

        var individualsCategories = groupedCategories
            .Where(x => x.ForVacancy && !x.IsOther)
            .ToList();

        // Settings: order matches legacy (created_at, key). created_at isn't
        // exposed in the DTO so we order by created_at then key on the server.
        var settingRows = await _db.Settings
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Key)
            .Select(x => new { x.Key, x.Value })
            .ToListAsync(ct);

        var settings = new Dictionary<string, object?>(settingRows.Count);
        foreach (var row in settingRows)
        {
            settings[row.Key] = SniffSettingValue(row.Value);
        }

        // Translations dict for the requested locale only. Missing locale =>
        // empty object (no 404), so the public frontend can fall back to keys.
        var translationRows = await _db.Translations
            .AsNoTracking()
            .Where(x => x.Locale == locale && x.IsActive)
            .Select(x => new { x.Key, x.Value })
            .ToListAsync(ct);

        var translations = translationRows
            .GroupBy(x => x.Key)
            .ToDictionary(g => g.Key, g => g.First().Value);

        return new InitDataResponse
        {
            Translations = translations,
            Settings = settings,
            SuggestedAttendee = suggestedAttendees,
            SuggestedLocations = suggestedLocations,
            EventTypes = eventTypes,
            OpportunityCategories = opportunityCategories,
            GroupedOpportunityCategories = groupedCategories,
            Cities = cities,
            Regions = regions,
            Languages = new LanguagesWrapperDto(languages),
            Banks = banks,
            Seasons = seasons,
            IndividualsOpportunityCategories = individualsCategories,
            Nationalities = nationalities,
            JobTitles = jobTitles,
            CancellationReasons = cancellationReasons,
            RejectionReasons = rejectionReasons,
        };
    }

    /// <summary>
    /// Reproduces the legacy InitDataController's value-sniffing: "true" /
    /// "false" / "1" / "0" become bool; numerics become int or decimal; JSON
    /// becomes a parsed structure; everything else stays a raw string.
    /// </summary>
    internal static object? SniffSettingValue(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var value = raw;

        switch (value)
        {
            case "true" or "1":
                return true;
            case "false" or "0":
                return false;
        }

        // Numeric — integer first (no decimal point), then decimal.
        if (!value.Contains('.') &&
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var asInt))
        {
            return asInt;
        }

        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var asDec))
        {
            return asDec;
        }

        // JSON — only attempt if it starts with a structural token.
        if (value.Length > 0 && (value[0] == '{' || value[0] == '['))
        {
            try
            {
                using var doc = JsonDocument.Parse(value);
                return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText());
            }
            catch (JsonException)
            {
                // Not JSON — fall through.
            }
        }

        return value;
    }
}
