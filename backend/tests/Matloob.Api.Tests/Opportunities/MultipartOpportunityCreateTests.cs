using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Common;
using Matloob.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// The standalone "add opportunity to an existing event" page posts MULTIPART
/// (laravel-precognition + files): <c>event_uuid</c> + nested
/// <c>opportunities[i][…]</c> with per-item <c>success_criteria</c> + uploads.
/// Verifies the multipart branch of <c>POST me/opportunities</c> persists
/// criteria + uploads, ADDS to the event (does not replace), short-circuits
/// precognition, and 422s an unknown event.
/// </summary>
public sealed class MultipartOpportunityCreateTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;
    private Guid _establishmentId;
    private Guid _nonVacancyCategoryId;

    private static readonly byte[] PngBytes =
        { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01 };

    public MultipartOpportunityCreateTests(OpportunitiesApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);
        _establishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-MULTIPART-OPP");

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Non-vacancy category so success criteria are persisted (legacy rule).
        _nonVacancyCategoryId = await db.OpportunityCategories
            .Where(c => !c.ForVacancy && !c.IsOther && c.ParentId != null)
            .Select(c => c.Id).FirstAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Multipart_WithCriteriaAndUploads_Creates_AndPersists()
    {
        var eventId = await SeedEventAsync();
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);

        using var form = BuildOpportunityForm(eventId, "Ushers needed", withCriterion: true, withUpload: true);
        var response = await client.PostAsync("/api/establishments/me/opportunities", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.DataOf();
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        Assert.Equal(1, data.GetArrayLength());

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var opp = await db.Opportunities.SingleAsync(o => o.EventId == eventId);
        Assert.Equal("Ushers needed", opp.Name);
        Assert.Equal(1, await db.SuccessManagementCriteria.CountAsync(c => c.OpportunityId == opp.Id));
        Assert.Equal(1, await db.OpportunityAssets.CountAsync(a => a.OpportunityId == opp.Id));
    }

    [Fact]
    public async Task Multipart_AddsToExistingEvent_DoesNotReplace()
    {
        var eventId = await SeedEventAsync();
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);

        using (var first = BuildOpportunityForm(eventId, "First opportunity", withCriterion: false, withUpload: false))
            (await client.PostAsync("/api/establishments/me/opportunities", first)).EnsureSuccessStatusCode();
        using (var second = BuildOpportunityForm(eventId, "Second opportunity", withCriterion: false, withUpload: false))
            (await client.PostAsync("/api/establishments/me/opportunities", second)).EnsureSuccessStatusCode();

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // ADD semantics: both opportunities survive (not delete-then-create).
        Assert.Equal(2, await db.Opportunities.CountAsync(o => o.EventId == eventId));
    }

    [Fact]
    public async Task Multipart_Precognition_StopsBeforeCreate_204()
    {
        var eventId = await SeedEventAsync();
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);

        using var form = BuildOpportunityForm(eventId, "Precog opportunity", withCriterion: false, withUpload: false);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/establishments/me/opportunities") { Content = form };
        req.Headers.Add("Precognition", "true");
        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Opportunities.CountAsync(o => o.EventId == eventId));
    }

    [Fact]
    public async Task Multipart_UnknownEvent_Returns422()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);

        using var form = BuildOpportunityForm(Guid.NewGuid(), "Orphan opportunity", withCriterion: false, withUpload: false);
        var response = await client.PostAsync("/api/establishments/me/opportunities", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("event_uuid", out _));
    }

    // -- helpers --------------------------------------------------------------

    private async Task<Guid> SeedEventAsync()
    {
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ev = new Event(Guid.NewGuid(), _establishmentId, Guid.NewGuid(), "Host Event", "An event.", null);
        db.Events.Add(ev);
        await db.SaveChangesAsync();
        return ev.Id;
    }

    private MultipartFormDataContent BuildOpportunityForm(
        Guid eventId, string name, bool withCriterion, bool withUpload)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var form = new MultipartFormDataContent
        {
            { new StringContent(eventId.ToString()), "event_uuid" },
            { new StringContent(_nonVacancyCategoryId.ToString()), "opportunities[0][opportunity_category_uuid]" },
            { new StringContent(name), "opportunities[0][name]" },
            { new StringContent("A detailed opportunity description for validation."), "opportunities[0][description]" },
            { new StringContent(today.AddDays(5).ToString("yyyy-MM-dd")), "opportunities[0][start_date]" },
            { new StringContent(today.AddDays(15).ToString("yyyy-MM-dd")), "opportunities[0][end_date]" },
            { new StringContent("Riyadh"), "opportunities[0][location_title]" },
            { new StringContent("24.7"), "opportunities[0][lat]" },
            { new StringContent("46.6"), "opportunities[0][lon]" },
            { new StringContent("3"), "opportunities[0][required_personnel]" },
        };

        if (withCriterion)
        {
            form.Add(new StringContent("Crowd flow managed"), "opportunities[0][success_criteria][0][output]");
            form.Add(new StringContent("All entrances staffed and queues kept under ten minutes."),
                "opportunities[0][success_criteria][0][success_criteria]");
        }
        if (withUpload)
        {
            AddFile(form, "opportunities[0][uploads][]", "brief.png");
            AddFile(form, "opportunities[0][success_criteria][0][uploads][]", "criterion.png");
        }

        return form;
    }

    private static void AddFile(MultipartFormDataContent form, string name, string fileName)
    {
        var file = new ByteArrayContent(PngBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, name, fileName);
    }
}
