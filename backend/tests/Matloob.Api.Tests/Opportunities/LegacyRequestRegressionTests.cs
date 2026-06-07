using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Domain.Establishments;
using Matloob.Domain.Offers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// Focused regression suite that proves every legacy Laravel
/// request shape continues to work on the new backend using ONLY the
/// legacy URL and ONLY the old field names — never the new canonical
/// shapes.
///
/// <para>
/// One test per audit finding so a future change that breaks a legacy
/// contract fails loudly with a recognisable name.
/// </para>
/// </summary>
public sealed class LegacyRequestRegressionTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private static readonly string[] ForbiddenSubstrings =
    [
        "ajeer", "contract", "invoice", "notice_path", "show_print_notice",
    ];

    private readonly OpportunitiesApiFactory _factory;
    private Guid _establishmentId;
    private Guid _vacancyCategoryId;

    private static readonly TestUser SponsorOwner = new(
        Sub: "legacy-regression-sponsor-owner", Roles: new[] { "matloob_user" });
    private Guid _sponsorEstablishmentId;

    public LegacyRequestRegressionTests(OpportunitiesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, SponsorOwner.Sub);

        _establishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-LEGACY-REG");
        _sponsorEstablishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, SponsorOwner.Sub, "CR-LEGACY-REG-SP");

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _vacancyCategoryId = await db.OpportunityCategories
            .Where(c => c.ForVacancy && !c.IsOther && c.ParentId != null)
            .Select(c => c.Id).FirstAsync();
        var sponsor = await db.Establishments.FirstAsync(e => e.Id == _sponsorEstablishmentId);
        typeof(Establishment).GetProperty(nameof(Establishment.IsSponsor))!
            .SetValue(sponsor, true);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -- AUDIT FINDING #1: bulk opportunity create -------------------------

    [Fact]
    public async Task LegacyBulk_CreateOpportunities_AtLegacyUrl_WithLaravelFieldNames()
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
                        name = "Audit fix #1",
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
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AssertNoForbiddenKeys(await response.Content.ReadAsStringAsync(), "bulk create");
    }

    // -- AUDIT FINDING #2: bare establishment reject -----------------------

    [Fact]
    public async Task LegacyBare_EstablishmentRejectOffer_AtLegacyUrl_NoBody()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _establishmentId, name: "Audit fix #2", forVacancy: false);
        var applyingOwner = new TestUser(
            Sub: "legacy-regression-applicant", Roles: new[] { "matloob_user" });
        await OaoHelpers.SeedLocalUserAsync(_factory, applyingOwner.Sub);
        var applyingEstId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, applyingOwner.Sub, "CR-LEGACY-REG-APPL");
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, oppId,
            applicantEstablishmentId: applyingEstId,
            appliedByUserId: applyingOwner.Sub);
        var offerId = await OaoHelpers.SeedOfferAsync(
            _factory, _establishmentId, oppId, appId,
            sentByUserId: OaoHelpers.EstablishmentOwner.Sub,
            status: OfferStatus.Pending);

        var client = _factory.CreateClientFor(applyingOwner);
        var response = await client.PostAsync(
            $"/api/establishments/offers/{offerId}/reject?establishment_id={applyingEstId}",
            content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoForbiddenKeys(await response.Content.ReadAsStringAsync(), "establishment reject bare");
    }

    // -- AUDIT FINDING #3: multipart establishment evaluation --------------

    [Fact]
    public async Task LegacyMultipart_EstablishmentEvaluation_AtLegacyUrl_WithUploads()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _establishmentId, name: "Audit fix #3", forVacancy: true);
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, oppId, applicantUserId: OaoHelpers.Worker.Sub);
        var offerId = await OaoHelpers.SeedOfferAsync(
            _factory, _establishmentId, oppId, appId,
            sentByUserId: OaoHelpers.EstablishmentOwner.Sub,
            status: OfferStatus.Accepted,
            acceptedAt: DateTimeOffset.UtcNow);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(offerId.ToString()), "offer_id");
        content.Add(new StringContent("5"), "rating");
        content.Add(new StringContent("true"), "recommend_for_future_opportunities");
        var fileBytes = Encoding.UTF8.GetBytes("%PDF audit fix");
        var file = new ByteArrayContent(fileBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "uploads[]", "audit-evidence.pdf");

        // Send to LEGACY URL with establishment context via header.
        var req = new HttpRequestMessage(HttpMethod.Post,
            "/api/establishments/evaluations")
        {
            Content = content,
        };
        req.Headers.Add("X-Commissioner-UUID", _establishmentId.ToString());

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AssertNoForbiddenKeys(await response.Content.ReadAsStringAsync(), "multipart evaluation");
    }

    // -- AUDIT FINDING #4: X-Commissioner-UUID context header --------------

    [Fact]
    public async Task LegacyHeader_XCommissionerUuid_ResolvesEstablishmentContext()
    {
        // Hit a legacy establishment route using ONLY the X-Commissioner-UUID
        // header for context — no ?establishment_id, no X-Establishment-Id.
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var req = new HttpRequestMessage(HttpMethod.Get,
            "/api/establishments/me/opportunities");
        req.Headers.Add("X-Commissioner-UUID", _establishmentId.ToString());
        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoForbiddenKeys(await response.Content.ReadAsStringAsync(), "X-Commissioner-UUID");
    }

    // -- AUDIT FINDING: apply endpoints are bare POSTs ---------------------

    [Fact]
    public async Task LegacyBare_UserApply_AtLegacyUrl_NoBody()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _establishmentId, name: "Apply target", forVacancy: true);

        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.PostAsync(
            $"/api/users/opportunities/{oppId}/apply", content: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // -- AUDIT FINDING: offer cancel body shape stayed compatible ----------

    [Fact]
    public async Task LegacyBody_CancelOffer_OldFieldNames()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _establishmentId, name: "Cancel target", forVacancy: true);
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, oppId, applicantUserId: OaoHelpers.Worker.Sub);
        var offerId = await OaoHelpers.SeedOfferAsync(
            _factory, _establishmentId, oppId, appId,
            sentByUserId: OaoHelpers.EstablishmentOwner.Sub,
            status: OfferStatus.Accepted,
            acceptedAt: DateTimeOffset.UtcNow);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reasonId = await db.OfferCancellationReasons.Select(r => r.Id).FirstAsync();

        // LEGACY URL, LEGACY body shape: { offer_id, reason_id }.
        var client = _factory.CreateClientFor(OaoHelpers.Worker);
        var response = await client.PostAsJsonAsync(
            "/api/users/offers/cancel",
            new { offer_id = offerId, reason_id = reasonId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoForbiddenKeys(await response.Content.ReadAsStringAsync(), "cancel offer");
    }

    // -- AUDIT FINDING: user evaluation tolerates extra Laravel fields -----

    [Fact]
    public async Task LegacyBody_UserEvaluation_OldFieldNames_ExtraFieldsTolerated()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _establishmentId, name: "Eval target", forVacancy: true);
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, oppId, applicantUserId: OaoHelpers.Worker.Sub);
        var offerId = await OaoHelpers.SeedOfferAsync(
            _factory, _establishmentId, oppId, appId,
            sentByUserId: OaoHelpers.EstablishmentOwner.Sub,
            status: OfferStatus.Accepted,
            acceptedAt: DateTimeOffset.UtcNow);

        var client = _factory.CreateClientFor(OaoHelpers.Worker);

        // Send the FULL Laravel payload — extra evaluable_id +
        // opportunity_id fields should be tolerated (System.Text.Json
        // ignores unknown JSON props by default).
        var response = await client.PostAsJsonAsync(
            "/api/users/evaluations",
            new
            {
                evaluable_id = _establishmentId,
                opportunity_id = oppId,
                offer_id = offerId,
                rating = 4,
                comment = "Solid run",
                recommend_for_future_opportunities = true,
                matloob_evaluation = 4,
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AssertNoForbiddenKeys(await response.Content.ReadAsStringAsync(), "user evaluation");
    }

    // -- AUDIT FINDING: response sweep across migrated routes --------------

    [Fact]
    public async Task LegacyResponseSweep_NoForbiddenKeys_AcrossAllMigratedReads()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);

        // /api/init-data is intentionally NOT in the sweep — it carries
        // the legacy `settings.ajeer_enabled` key which the data-migration
        // plan (Q-AJ-3) drops at import time, not at seed time. The sweep
        // covers the OAO responses where forbidden keys would be a true
        // regression.
        string[] urls =
        [
            "/api/users/profile",
            "/api/users/profile/establishment-list",
            "/api/users/opportunities",
        ];
        var worker = _factory.CreateClientFor(OaoHelpers.Worker);

        foreach (var url in urls)
        {
            var response = await worker.GetAsync(url);
            Assert.True(response.IsSuccessStatusCode,
                $"Read endpoint {url} failed with {response.StatusCode}.");
            AssertNoForbiddenKeys(await response.Content.ReadAsStringAsync(), url);
        }
    }

    // -- helpers -----------------------------------------------------------

    private static void AssertNoForbiddenKeys(string json, string where)
    {
        if (string.IsNullOrEmpty(json)) return;
        using var doc = JsonDocument.Parse(json);
        foreach (var key in EnumerateKeys(doc.RootElement))
        {
            // contracts_count (filled positions) substring-matches "contract"
            // but is a legitimate frontend field, not the dropped Ajeer concept.
            if (key.Equals("contracts_count", StringComparison.OrdinalIgnoreCase)) continue;
            var lower = key.ToLowerInvariant();
            foreach (var bad in ForbiddenSubstrings)
            {
                Assert.False(lower.Contains(bad),
                    $"{where} returned forbidden key '{key}' (matches '{bad}').");
            }
        }
    }

    private static IEnumerable<string> EnumerateKeys(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    yield return prop.Name;
                    foreach (var n in EnumerateKeys(prop.Value)) yield return n;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var n in EnumerateKeys(item)) yield return n;
                }
                break;
        }
    }
}
