using System.Net;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Establishments;
using Matloob.Domain.Admins;
using Matloob.Domain.Applications;
using Matloob.Domain.Common;
using Matloob.Domain.Establishments;
using Matloob.Domain.Offers;
using Matloob.Domain.Opportunities;
using Matloob.Domain.Reference;
using Matloob.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Matloob.Api.Tests.Admins;

/// <summary>
/// Integration tests for the admin list screens
/// (<c>/api/v1/admin/individuals</c>, <c>/establishments</c>, <c>/contracts</c>):
/// admin-only auth, the individuals/admins TPH split, the organizer/operator
/// filter, and the accepted-offer-as-contract set.
/// </summary>
public sealed class AdminListsTests : IClassFixture<EstablishmentsApiFactory>
{
    private readonly EstablishmentsApiFactory _factory;

    private static readonly TestUser Admin = new(
        Sub: "admin-lists-admin",
        Roles: new[] { "matloob_admin" },
        Audiences: new[] { "matloob:admin" });

    private static readonly TestUser PlainUser = new(
        Sub: "admin-lists-user",
        Roles: new[] { "matloob_user" });

    public AdminListsTests(EstablishmentsApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/api/v1/admin/individuals")]
    [InlineData("/api/v1/admin/establishments")]
    [InlineData("/api/v1/admin/contracts")]
    public async Task Endpoints_AreAdminOnly(string route)
    {
        var anon = _factory.CreateClientFor(null);
        var user = _factory.CreateClientFor(PlainUser);
        var admin = _factory.CreateClientFor(Admin);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task Individuals_ExcludeAdmins_AndProjectLookupNames()
    {
        var now = DateTimeOffset.UtcNow;
        var cityId = Guid.NewGuid();
        var nationalityId = Guid.NewGuid();

        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Cities.Add(new City(cityId, "الرياض"));
            db.Nationalities.Add(new Nationality(nationalityId, "سعودي"));

            var indiv = User.CreateFromIdentity(
                Guid.NewGuid(), "indiv-projfields", "indiv-proj@test.co", "Individual Proj", "+966500000001", now);
            indiv.SetIdentityAttributes("ID-12345", Gender.Male, 30, null, nationalityId);
            indiv.SetYearsOfExperience(7);
            indiv.UpdatePersonalInfo("Individual Proj", "indiv-proj@test.co", "+966500000001", null, null, cityId, null);
            db.Users.Add(indiv);

            db.Users.Add(new Admin(Guid.NewGuid(), "seeded-admin-row", "seeded-admin@test.co", "Seeded Admin"));
            await db.SaveChangesAsync();
        }

        var admin = _factory.CreateClientFor(Admin);
        var items = await GetItemsAsync(admin, "/api/v1/admin/individuals");

        var indivRow = items.FirstOrDefault(i =>
            i.TryGetProperty("email", out var e) && e.GetString() == "indiv-proj@test.co");
        Assert.True(indivRow.ValueKind == JsonValueKind.Object, "seeded individual should be listed");
        Assert.Equal("ID-12345", indivRow.GetProperty("idNumber").GetString());
        Assert.Equal("ذكر", indivRow.GetProperty("gender").GetString());
        Assert.Equal(30, indivRow.GetProperty("age").GetInt32());
        Assert.Equal("الرياض", indivRow.GetProperty("city").GetString());
        Assert.Equal("سعودي", indivRow.GetProperty("nationality").GetString());
        Assert.Equal(7, indivRow.GetProperty("yearsOfExperience").GetInt32());

        // The TPH Admin row must NOT appear in the individuals list.
        Assert.DoesNotContain(items, i =>
            i.TryGetProperty("email", out var e) && e.GetString() == "seeded-admin@test.co");
    }

    [Fact]
    public async Task Establishments_RoleFilter_SplitsOrganizerVsOperator()
    {
        var now = DateTimeOffset.UtcNow;
        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Establishments.Add(BuildApproved("Org Filter Co", "CR-ORGF", canManageEvents: true, now));
            db.Establishments.Add(BuildApproved("Operator Filter Co", "CR-OPF", canManageEvents: false, now));
            await db.SaveChangesAsync();
        }

        var admin = _factory.CreateClientFor(Admin);

        var organizers = await GetItemsAsync(admin, "/api/v1/admin/establishments?role=organizer");
        Assert.Contains(organizers, e => e.GetProperty("name").GetString() == "Org Filter Co");
        Assert.DoesNotContain(organizers, e => e.GetProperty("name").GetString() == "Operator Filter Co");
        Assert.All(organizers, e => Assert.True(e.GetProperty("canManageEvents").GetBoolean()));

        var operators = await GetItemsAsync(admin, "/api/v1/admin/establishments?role=operator");
        Assert.Contains(operators, e => e.GetProperty("name").GetString() == "Operator Filter Co");
        Assert.DoesNotContain(operators, e => e.GetProperty("name").GetString() == "Org Filter Co");
        Assert.All(operators, e => Assert.False(e.GetProperty("canManageEvents").GetBoolean()));

        // No filter => both surface.
        var all = await GetItemsAsync(admin, "/api/v1/admin/establishments");
        Assert.Contains(all, e => e.GetProperty("name").GetString() == "Org Filter Co");
        Assert.Contains(all, e => e.GetProperty("name").GetString() == "Operator Filter Co");
    }

    [Fact]
    public async Task Contracts_ListAcceptedOffers_WithNames_AndExcludePending()
    {
        var now = DateTimeOffset.UtcNow;
        var start = DateOnly.FromDateTime(now.UtcDateTime);
        var end = start.AddDays(20);

        using (var scope = _factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var provider = BuildApproved("Provider Co", "CR-PROV", canManageEvents: true, now);
            db.Establishments.Add(provider);

            var opp = Opportunity.Create(
                Guid.NewGuid(), provider.Id, Guid.NewGuid(), Guid.NewGuid(),
                "Contract Opp", "desc", start, end, "loc", 0m, 0m, 1, now);
            db.Opportunities.Add(opp);

            db.Users.Add(User.CreateFromIdentity(
                Guid.NewGuid(), "contract-applicant", "applicant-c@test.co", "Contract Applicant", null, now));

            // Accepted contract.
            var acceptedApp = OpportunityApplication.ForUser(Guid.NewGuid(), opp.Id, "contract-applicant");
            db.OpportunityApplications.Add(acceptedApp);
            var accepted = Offer.Create(
                Guid.NewGuid(), provider.Id, opp.Id, acceptedApp.Id, "sender-user",
                now, now.AddDays(5), start, end, monthlySalary: 6000m);
            accepted.Accept(now);
            db.Offers.Add(accepted);

            // Pending offer — must NOT appear as a contract.
            var pendingApp = OpportunityApplication.ForUser(Guid.NewGuid(), opp.Id, "contract-applicant");
            db.OpportunityApplications.Add(pendingApp);
            var pending = Offer.Create(
                Guid.NewGuid(), provider.Id, opp.Id, pendingApp.Id, "sender-user",
                now, now.AddDays(5), start, end, monthlySalary: 7000m);
            db.Offers.Add(pending);

            await db.SaveChangesAsync();
        }

        var admin = _factory.CreateClientFor(Admin);
        var items = await GetItemsAsync(admin, "/api/v1/admin/contracts");

        var row = items.FirstOrDefault(i =>
            i.TryGetProperty("opportunityName", out var o) && o.GetString() == "Contract Opp"
            && i.GetProperty("status").GetString() == "accepted");
        Assert.True(row.ValueKind == JsonValueKind.Object, "accepted offer should appear as a contract");
        Assert.Equal("Provider Co", row.GetProperty("providerName").GetString());
        Assert.Equal("Contract Applicant", row.GetProperty("applicantName").GetString());
        Assert.Equal("user", row.GetProperty("applicantType").GetString());
        Assert.Equal(6000, row.GetProperty("monthlySalary").GetDecimal());

        // The Pending offer (salary 7000) is excluded by the contract status set.
        Assert.DoesNotContain(items, i =>
            i.TryGetProperty("monthlySalary", out var m)
            && m.ValueKind == JsonValueKind.Number && m.GetDecimal() == 7000);
    }

    // --- helpers ------------------------------------------------------------

    private static Establishment BuildApproved(string name, string cr, bool canManageEvents, DateTimeOffset now)
    {
        var est = Establishment.CreateDraft(Guid.NewGuid(), "seed-creator");
        est.UpdateBasicInfo(
            name: FieldChange.SetTo<string?>(name),
            commercialRegistrationNumber: FieldChange.SetTo<string?>(cr),
            laborOfficeId: FieldChange.SetTo<string?>("100"),
            sequenceNumber: FieldChange.SetTo<string?>("200"),
            city: FieldChange.SetTo<string?>("Riyadh"),
            email: FieldChange.SetTo<string?>($"{cr.ToLower()}@test.co"),
            phone: FieldChange.SetTo<string?>("+966500000000"),
            economicActivity: FieldChange.SetTo<string?>("Events"),
            canManageEvents: FieldChange.SetTo<bool?>(canManageEvents));
        est.SubmitForReview(now);
        est.Approve(now, "seed-admin");
        return est;
    }

    private static async Task<List<JsonElement>> GetItemsAsync(HttpClient client, string route)
    {
        var resp = await client.GetAsync(route);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;
        if (root.TryGetProperty("data", out var data)) root = data;
        // Clone so the elements survive the JsonDocument's disposal.
        return root.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
    }
}
