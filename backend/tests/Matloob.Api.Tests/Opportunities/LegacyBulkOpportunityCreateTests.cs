using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// Verifies the legacy Laravel bulk shape
/// (<c>{event_uuid, opportunities:[...]}</c> with
/// <c>opportunity_category_uuid</c> per item) is accepted by
/// <c>POST /api/establishments/me/opportunities</c> alongside the
/// canonical single-object shape.
/// </summary>
public sealed class LegacyBulkOpportunityCreateTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;
    private Guid _establishmentId;
    private Guid _vacancyCategoryId;

    public LegacyBulkOpportunityCreateTests(OpportunitiesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);
        _establishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-LEGACY-BULK-OPP");

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _vacancyCategoryId = await db.OpportunityCategories
            .Where(c => c.ForVacancy && !c.IsOther && c.ParentId != null)
            .Select(c => c.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task LegacyBulk_OneOpportunity_ReturnsArray()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            "/api/establishments/me/opportunities",
            new
            {
                event_uuid = Guid.NewGuid(),
                opportunities = new[]
                {
                    new
                    {
                        opportunity_category_uuid = _vacancyCategoryId,
                        name = "Bulk legacy 1",
                        description = "Description text long enough for validation.",
                        start_date = today.AddDays(5).ToString("yyyy-MM-dd"),
                        end_date = today.AddDays(15).ToString("yyyy-MM-dd"),
                        location_title = "Riyadh",
                        lat = 24.7m,
                        lon = 46.6m,
                        required_personnel = 3,
                    },
                },
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        var first = doc.RootElement.EnumerateArray().First();
        Assert.Equal("Bulk legacy 1", first.GetProperty("name").GetString());
        // No Ajeer/contract fields.
        Assert.False(first.TryGetProperty("contract", out _));
        Assert.False(first.TryGetProperty("contracts_count", out _));
    }

    [Fact]
    public async Task LegacyBulk_MultipleOpportunities_AllCreated()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            "/api/establishments/me/opportunities",
            new
            {
                event_uuid = Guid.NewGuid(),
                opportunities = new[]
                {
                    new
                    {
                        opportunity_category_uuid = _vacancyCategoryId,
                        name = "Bulk legacy A",
                        description = "Description text long enough for validation.",
                        start_date = today.AddDays(5).ToString("yyyy-MM-dd"),
                        end_date = today.AddDays(15).ToString("yyyy-MM-dd"),
                        location_title = "Riyadh",
                        lat = 24.7m,
                        lon = 46.6m,
                        required_personnel = 5,
                    },
                    new
                    {
                        opportunity_category_uuid = _vacancyCategoryId,
                        name = "Bulk legacy B",
                        description = "Description text long enough for validation.",
                        start_date = today.AddDays(6).ToString("yyyy-MM-dd"),
                        end_date = today.AddDays(20).ToString("yyyy-MM-dd"),
                        location_title = "Jeddah",
                        lat = 21.5m,
                        lon = 39.2m,
                        required_personnel = 10,
                    },
                },
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
        var names = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("Bulk legacy A", names);
        Assert.Contains("Bulk legacy B", names);
    }

    [Fact]
    public async Task LegacyBulk_MissingEventUuid_Returns400()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            "/api/establishments/me/opportunities",
            new
            {
                opportunities = new[]
                {
                    new
                    {
                        opportunity_category_uuid = _vacancyCategoryId,
                        name = "No event",
                        description = "Description text long enough for validation.",
                        start_date = today.AddDays(5).ToString("yyyy-MM-dd"),
                        end_date = today.AddDays(15).ToString("yyyy-MM-dd"),
                        location_title = "Riyadh",
                        lat = 24.7m,
                        lon = 46.6m,
                        required_personnel = 1,
                    },
                },
            });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LegacyBulk_UnknownCategoryUuid_Returns422()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            "/api/establishments/me/opportunities",
            new
            {
                event_uuid = Guid.NewGuid(),
                opportunities = new[]
                {
                    new
                    {
                        opportunity_category_uuid = Guid.NewGuid(),
                        name = "Bad cat",
                        description = "Description text long enough for validation.",
                        start_date = today.AddDays(5).ToString("yyyy-MM-dd"),
                        end_date = today.AddDays(15).ToString("yyyy-MM-dd"),
                        location_title = "Riyadh",
                        lat = 24.7m,
                        lon = 46.6m,
                        required_personnel = 1,
                    },
                },
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CanonicalSingle_StillWorks_ReturnsSingleObject()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities",
            new
            {
                event_id = Guid.NewGuid(),
                opportunity_category_id = _vacancyCategoryId,
                name = "Single shape",
                description = "Description text long enough.",
                start_date = today.AddDays(5).ToString("yyyy-MM-dd"),
                end_date = today.AddDays(15).ToString("yyyy-MM-dd"),
                location_title = "Riyadh",
                lat = 24.7m,
                lon = 46.6m,
                required_personnel = 4,
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        // Single object, not array.
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.Equal("Single shape", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task LegacyAndCanonical_BothAccept_EventUuidAlias()
    {
        // Either route accepts both event_id and event_uuid keys
        // because the polymorphic DTO has both aliases.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            "/api/establishments/me/opportunities",
            new
            {
                event_uuid = Guid.NewGuid(),
                opportunity_category_uuid = _vacancyCategoryId,
                name = "Single via legacy URL with uuid keys",
                description = "Description text long enough.",
                start_date = today.AddDays(5).ToString("yyyy-MM-dd"),
                end_date = today.AddDays(15).ToString("yyyy-MM-dd"),
                location_title = "Riyadh",
                lat = 24.7m,
                lon = 46.6m,
                required_personnel = 2,
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        // Single object (no opportunities[] wrapper).
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }
}
