using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Common;
using Matloob.Domain.Opportunities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Matloob.Api.Infrastructure.Persistence;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// Tests for the Phase OAO-3 owner-side opportunity write endpoints:
/// create / update / delete / end / asset link.
/// </summary>
public sealed class OpportunityWriteTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;
    private Guid _establishmentId;
    private Guid _vacancyCategoryId;

    public OpportunityWriteTests(OpportunitiesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Outsider.Sub);

        _establishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-OAO-WRITE-A");

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _vacancyCategoryId = await db.OpportunityCategories
            .Where(c => c.ForVacancy && !c.IsOther && c.ParentId != null)
            .Select(c => c.Id)
            .FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private object MakeCreatePayload(
        Guid? categoryId = null,
        string name = "Servers",
        string? description = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int requiredPersonnel = 10)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new
        {
            event_id = Guid.NewGuid(),
            opportunity_category_id = categoryId ?? _vacancyCategoryId,
            name,
            description = description ?? "Long-enough description for the opportunity.",
            start_date = (startDate ?? today.AddDays(5)).ToString("yyyy-MM-dd"),
            end_date = (endDate ?? today.AddDays(15)).ToString("yyyy-MM-dd"),
            location_title = "Riyadh",
            lat = 24.7m,
            lon = 46.6m,
            required_personnel = requiredPersonnel,
            monthly_salary = 5000m,
            establishment_classification = new[] { "small", "medium" },
            gender = new[] { "male", "female" },
        };
    }

    // -- create -------------------------------------------------------------

    [Fact]
    public async Task Create_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var response = await anon.PostAsJsonAsync(
            "/api/establishments/me/opportunities", MakeCreatePayload());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_NonMember_Returns404()
    {
        var outsider = _factory.CreateClientFor(OaoHelpers.Outsider);
        var response = await outsider.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities",
            MakeCreatePayload());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_HappyPath_Returns201_AndPersistsOpportunity()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities",
            MakeCreatePayload(name: "Created opp"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var data = doc.RootElement.DataOf();
        var id = data.GetProperty("id").GetGuid();
        Assert.Equal("Created opp", data.GetProperty("name").GetString());
        Assert.Equal("Upcoming", data.GetProperty("status").GetString());

        var persisted = await OaoHelpers.LoadOpportunityAsync(_factory, id);
        Assert.NotNull(persisted);
        Assert.Equal(_establishmentId, persisted!.IssuerEstablishmentId);

        // Outbox event emitted.
        var outboxCount = await OaoHelpers.CountOutboxEventsAsync(
            _factory, OpportunityEventTypes.Created, id);
        Assert.Equal(1, outboxCount);
    }

    [Theory]
    [InlineData("full_time")]
    [InlineData("part_time")]
    public async Task Create_WorkingHoursType_RoundTripsWireToken(string wireToken)
    {
        // Regression: the WorkingHoursType enum was Fixed/Flexible/Shifts, so
        // Enum.TryParse("full_time") failed and the value was silently dropped on
        // every opportunity. It must round-trip the legacy/frontend wire token.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities",
            new
            {
                event_id = Guid.NewGuid(),
                opportunity_category_id = _vacancyCategoryId,
                name = "WH opp",
                description = "Long-enough description for the opportunity.",
                start_date = today.AddDays(5).ToString("yyyy-MM-dd"),
                end_date = today.AddDays(15).ToString("yyyy-MM-dd"),
                location_title = "Riyadh",
                lat = 24.7m,
                lon = 46.6m,
                required_personnel = 3,
                monthly_salary = 5000m,
                working_hours_type = wireToken,
                working_hours_from = "09:00:00",
                working_hours_to = "17:00:00",
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.DataOf();
        Assert.Equal(wireToken, data.GetProperty("working_hours_type").GetString());

        // And it actually persisted (EF converter round-trip), not just echoed.
        var id = data.GetProperty("id").GetGuid();
        var persisted = await OaoHelpers.LoadOpportunityAsync(_factory, id);
        Assert.Equal(
            Matloob.Domain.Opportunities.WorkingHoursTypeWire.Parse(wireToken),
            persisted!.WorkingHoursType);
    }

    [Fact]
    public async Task Create_StartDateInPast_StartsActive()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities",
            MakeCreatePayload(
                name: "Active opp",
                startDate: today.AddDays(-2),
                endDate: today.AddDays(5)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("Active", doc.RootElement.DataOf().GetProperty("status").GetString());
    }

    [Fact]
    public async Task Create_InvalidCategory_Returns422()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities",
            MakeCreatePayload(categoryId: Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("opportunity_category_not_found",
            doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_ValidationFailure_Returns422()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities",
            new
            {
                event_id = Guid.NewGuid(),
                opportunity_category_id = _vacancyCategoryId,
                name = "", // invalid
                description = "Long-enough description text.",
                start_date = "2026-12-01",
                end_date = "2026-12-10",
                location_title = "Riyadh",
                lat = 24.7m,
                lon = 46.6m,
                required_personnel = 1,
            });
        // FluentValidation failures now return Laravel-style 422.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // -- update -------------------------------------------------------------

    [Fact]
    public async Task Update_Name_PatchesAndPersists()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "Before");

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities/{oppId}",
            new { name = "After" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("After", doc.RootElement.DataOf().GetProperty("name").GetString());

        var persisted = await OaoHelpers.LoadOpportunityAsync(_factory, oppId);
        Assert.Equal("After", persisted!.Name);
    }

    [Fact]
    public async Task Update_ForeignOpportunity_Returns404()
    {
        // Create an opportunity from another establishment first.
        var otherOwner = new TestUser(
            Sub: "oao-write-other-owner", Roles: new[] { "matloob_user" });
        await OaoHelpers.SeedLocalUserAsync(_factory, otherOwner.Sub);
        var otherEstId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, otherOwner.Sub, "CR-OAO-WRITE-OTHER");
        var foreignOpp = await OaoHelpers.SeedOpportunityAsync(_factory, otherEstId,
            name: "Foreign");

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities/{foreignOpp}",
            new { name = "Should fail" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -- delete -------------------------------------------------------------

    [Fact]
    public async Task Delete_OwnOpportunity_Returns204_AndSoftDeletes()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "Doomed");

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.DeleteAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities/{oppId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Soft-deleted row should be filtered from default queries.
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(await db.Opportunities.FirstOrDefaultAsync(o => o.Id == oppId));
        Assert.NotNull(await db.Opportunities.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == oppId));
    }

    // -- end ----------------------------------------------------------------

    [Fact]
    public async Task End_Upcoming_FlipsToEnded()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "End me", status: OpportunityStatus.Upcoming);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PatchAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities/{oppId}/end",
            content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("Ended", doc.RootElement.DataOf().GetProperty("status").GetString());
    }

    [Fact]
    public async Task End_AlreadyEnded_Returns422()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "Already ended", status: OpportunityStatus.Ended);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PatchAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities/{oppId}/end",
            content: null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // -- asset link ---------------------------------------------------------

    [Fact]
    public async Task LinkAsset_OwnerOwnedAsset_Returns201()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "Asset link target");
        var assetId = await OaoHelpers.SeedAssetAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities/{oppId}/assets",
            new { asset_id = assetId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var data = doc.RootElement.DataOf();
        Assert.Equal(assetId, data.GetProperty("asset_id").GetGuid());
        Assert.False(data.TryGetProperty("relative_path", out _));
        Assert.False(data.TryGetProperty("stored_file_name", out _));
    }

    [Fact]
    public async Task LinkAsset_NotOwnedAsset_Returns422()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "Foreign asset");
        var foreignAssetId = await OaoHelpers.SeedAssetAsync(_factory, OaoHelpers.Outsider.Sub);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities/{oppId}/assets",
            new { asset_id = foreignAssetId });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("asset_not_owned_by_caller",
            doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task LinkAsset_UnknownAsset_Returns422()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(_factory, _establishmentId,
            name: "Unknown asset");

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/establishments/{_establishmentId}/opportunities/{oppId}/assets",
            new { asset_id = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("asset_not_found",
            doc.RootElement.GetProperty("code").GetString());
    }
}
