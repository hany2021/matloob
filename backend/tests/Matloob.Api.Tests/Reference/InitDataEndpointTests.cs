using System.Net;
using System.Text.Json;

namespace Matloob.Api.Tests.Reference;

/// <summary>
/// Integration tests for <c>GET /api/v1/init-data</c>. The endpoint is the
/// public-frontend's bootstrap call; this suite locks down its response shape
/// so future changes are caught at build time.
///
/// Database: in-memory (via <see cref="InitDataApiFactory"/>), seeded once
/// per factory with the canonical reference dataset.
/// </summary>
public sealed class InitDataEndpointTests : IClassFixture<InitDataApiFactory>, IAsyncLifetime
{
    private readonly InitDataApiFactory _factory;

    public InitDataEndpointTests(InitDataApiFactory factory)
    {
        _factory = factory;
    }

    // IAsyncLifetime: xUnit calls this before each test; seeding is idempotent
    // so the cost is one AnyAsync per table on the warm calls.
    public Task InitializeAsync() => _factory.EnsureSeededAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetInitData_Anonymous_ReturnsOk()
    {
        // No auth header — the endpoint is AllowAnonymous and must succeed.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/init-data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetInitData_ReturnsAllSeventeenTopLevelKeys()
    {
        var root = await GetRootAsync();

        // Locks the response surface. If a key is added/removed/renamed, this
        // test fails immediately — that is the point.
        string[] expected =
        {
            "translations",
            "settings",
            "suggested_attendee",
            "suggested_locations",
            "event_types",
            "opportunity_categories",
            "grouped_opportunity_categories",
            "cities",
            "regions",
            "languages",
            "banks",
            "seasons",
            "individuals_opportunity_categories",
            "nationalities",
            "job_titles",
            "cancellation_reasons",
            "rejection_reasons",
        };

        var actual = root.EnumerateObject().Select(p => p.Name).ToHashSet();
        foreach (var key in expected)
        {
            Assert.True(actual.Contains(key), $"missing top-level key '{key}'");
        }

        Assert.Equal(expected.Length, actual.Count);
    }

    [Fact]
    public async Task GetInitData_ListEndpointsMatchSeededCounts()
    {
        var root = await GetRootAsync();

        // Counts come straight from ReferenceDataSeeder. If the seeder changes,
        // update these in lockstep — they document what the public frontend
        // sees on a fresh database.
        Assert.Equal(21, root.GetProperty("cities").GetArrayLength());
        Assert.Equal(13, root.GetProperty("regions").GetArrayLength());
        Assert.Equal(11, root.GetProperty("banks").GetArrayLength());
        Assert.Equal(16, root.GetProperty("job_titles").GetArrayLength());
        Assert.Equal(217, root.GetProperty("nationalities").GetArrayLength());
        Assert.Equal(6, root.GetProperty("event_types").GetArrayLength());
        Assert.Equal(5, root.GetProperty("seasons").GetArrayLength());
        Assert.Equal(4, root.GetProperty("suggested_attendee").GetArrayLength());
        Assert.Equal(12, root.GetProperty("suggested_locations").GetArrayLength());
        Assert.Equal(54, root.GetProperty("opportunity_categories").GetArrayLength());
        Assert.Equal(7, root.GetProperty("grouped_opportunity_categories").GetArrayLength());
        Assert.Equal(2, root.GetProperty("individuals_opportunity_categories").GetArrayLength());
        Assert.Equal(3, root.GetProperty("cancellation_reasons").GetArrayLength());
        Assert.Equal(4, root.GetProperty("rejection_reasons").GetArrayLength());
    }

    [Fact]
    public async Task GetInitData_LanguagesUsesDataWrapper()
    {
        var root = await GetRootAsync();

        // Languages must be the {data: [...]} envelope, not a bare array —
        // matches the legacy Laravel paginator wrapper the public frontend
        // expects.
        var languages = root.GetProperty("languages");
        Assert.Equal(JsonValueKind.Object, languages.ValueKind);
        Assert.True(languages.TryGetProperty("data", out var data), "languages.data is missing");
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        Assert.Equal(4, data.GetArrayLength());
    }

    [Fact]
    public async Task GetInitData_SettingsAreTypeSniffed()
    {
        var root = await GetRootAsync();

        var settings = root.GetProperty("settings");
        Assert.Equal(JsonValueKind.Object, settings.ValueKind);

        // Seeder writes the literal strings "false" and "20"; the endpoint
        // must surface them as JSON bool and number, not raw strings.
        Assert.Equal(JsonValueKind.False, settings.GetProperty("ajeer_enabled").ValueKind);
        Assert.Equal(JsonValueKind.Number, settings.GetProperty("minimum_ajeer_contracts").ValueKind);
        Assert.Equal(20, settings.GetProperty("minimum_ajeer_contracts").GetInt32());
        Assert.Equal(JsonValueKind.Number, settings.GetProperty("minimum_saudi_contracts").ValueKind);
        Assert.Equal(50, settings.GetProperty("minimum_saudi_contracts").GetInt32());
    }

    [Fact]
    public async Task GetInitData_DefaultLocale_ReturnsEnglishTranslations()
    {
        var root = await GetRootAsync(locale: null);

        var translations = root.GetProperty("translations");
        Assert.Equal(JsonValueKind.Object, translations.ValueKind);
        Assert.Equal("Welcome", translations.GetProperty("welcome").GetString());
        Assert.Equal("Save", translations.GetProperty("save").GetString());
        Assert.Equal("Cancel", translations.GetProperty("cancel").GetString());
    }

    [Fact]
    public async Task GetInitData_ArabicLocale_ReturnsArabicTranslations()
    {
        var root = await GetRootAsync(locale: "ar");

        var translations = root.GetProperty("translations");
        Assert.Equal("مرحبا", translations.GetProperty("welcome").GetString());
        Assert.Equal("حفظ", translations.GetProperty("save").GetString());
        Assert.Equal("إلغاء", translations.GetProperty("cancel").GetString());
    }

    [Fact]
    public async Task GetInitData_UnknownLocale_ReturnsEmptyTranslationsDict()
    {
        var root = await GetRootAsync(locale: "fr");

        var translations = root.GetProperty("translations");
        Assert.Equal(JsonValueKind.Object, translations.ValueKind);
        // No fr rows seeded -> empty object, NOT 404. Frontend falls back to keys.
        Assert.Empty(translations.EnumerateObject());
    }

    [Fact]
    public async Task GetInitData_GroupedCategories_HaveChildrenPopulated()
    {
        var root = await GetRootAsync();

        var grouped = root.GetProperty("grouped_opportunity_categories");
        Assert.Equal(7, grouped.GetArrayLength());

        foreach (var parent in grouped.EnumerateArray())
        {
            Assert.True(parent.TryGetProperty("children", out var children),
                $"parent '{parent.GetProperty("title").GetString()}' has no 'children' key");
            Assert.Equal(JsonValueKind.Array, children.ValueKind);

            // Every seeded group has at least one child.
            Assert.True(children.GetArrayLength() > 0,
                $"parent '{parent.GetProperty("title").GetString()}' has zero children");
        }
    }

    [Fact]
    public async Task GetInitData_IndividualsCategories_AreForVacancyAndNotOther()
    {
        var root = await GetRootAsync();

        var individuals = root.GetProperty("individuals_opportunity_categories");
        Assert.Equal(2, individuals.GetArrayLength());

        foreach (var category in individuals.EnumerateArray())
        {
            Assert.True(category.GetProperty("for_vacancy").GetBoolean(),
                "individuals category must have for_vacancy=true");
            Assert.False(category.GetProperty("is_other").GetBoolean(),
                "individuals category must have is_other=false");
        }
    }

    [Fact]
    public async Task GetInitData_LegacyAliasRoute_ReturnsSamePayload()
    {
        var client = _factory.CreateClient();

        var v1 = await client.GetStringAsync("/api/v1/init-data");
        var alias = await client.GetStringAsync("/api/init-data");

        // Same database, same query, same cache key — bodies must be byte-equal.
        Assert.Equal(v1, alias);
    }

    private async Task<JsonElement> GetRootAsync(string? locale = null)
    {
        var client = _factory.CreateClient();
        var url = locale is null ? "/api/v1/init-data" : $"/api/v1/init-data?locale={locale}";

        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        // JsonDocument owns the buffer; Clone() to detach for the test body.
        return doc.RootElement.Clone();
    }
}
