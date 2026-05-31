using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Tests.Auth;
using Matloob.Api.Tests.Common;

namespace Matloob.Api.Tests.Establishments;

/// <summary>
/// Integration tests for the establishment profile-edit endpoints
/// (Phase 1 — no new schema): general-info (multipart), contact-info (JSON +
/// precognition) and experience (years_of_experience). Each verifies the edit
/// lands AND that <c>GET me/profile</c> reflects it, plus the 422 / 204 paths.
/// </summary>
public sealed class EstablishmentProfileEditTests
    : IClassFixture<EstablishmentsApiFactory>, IAsyncLifetime
{
    private readonly EstablishmentsApiFactory _factory;

    private static readonly TestUser GeneralUser = new("estab-prof-general-1", new[] { "matloob_user" });
    private static readonly TestUser ContactUser = new("estab-prof-contact-1", new[] { "matloob_user" });
    private static readonly TestUser PrecogUser = new("estab-prof-precog-1", new[] { "matloob_user" });
    private static readonly TestUser DupUser = new("estab-prof-dup-1", new[] { "matloob_user" });
    private static readonly TestUser ExperienceUser = new("estab-prof-exp-1", new[] { "matloob_user" });
    private static readonly TestUser ValidationUser = new("estab-prof-val-1", new[] { "matloob_user" });

    public EstablishmentProfileEditTests(EstablishmentsApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await Helpers.SeedLocalUserAsync(_factory, GeneralUser.Sub);
        await Helpers.SeedLocalUserAsync(_factory, ContactUser.Sub);
        await Helpers.SeedLocalUserAsync(_factory, PrecogUser.Sub);
        await Helpers.SeedLocalUserAsync(_factory, DupUser.Sub);
        await Helpers.SeedLocalUserAsync(_factory, ExperienceUser.Sub);
        await Helpers.SeedLocalUserAsync(_factory, ValidationUser.Sub);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -- general-info ---------------------------------------------------------

    [Fact]
    public async Task GeneralInfo_Updates_AndGetReflects()
    {
        var id = await BuildApprovedEstablishmentWithMember("CR-PROF-GEN", GeneralUser.Sub);
        var client = _factory.CreateClientFor(GeneralUser);

        using var form = new MultipartFormDataContent
        {
            { new StringContent("Premier events organiser in Riyadh."), "description" },
            { new StringContent("https://acme-events.test"), "website" },
            { new StringContent("4521"), "building_number" },
            { new StringContent("12345"), "postal_code" },
            { new StringContent("24.7136"), "lat" },
            { new StringContent("46.6753"), "lon" },
            { new StringContent("PATCH"), "_method" },
        };
        var response = await client.PostAsync($"/api/establishments/me/profile/general-info?establishment_id={id}", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var general = (await GetProfileAsync(client, id)).GetProperty("profile").GetProperty("general_info");
        Assert.Equal("Premier events organiser in Riyadh.", general.GetProperty("description").GetString());
        Assert.Equal("https://acme-events.test", general.GetProperty("website").GetString());
        Assert.Equal("4521", general.GetProperty("building_number").GetString());
        Assert.Equal("12345", general.GetProperty("postal_code").GetString());
        Assert.Equal(24.7136m, general.GetProperty("lat").GetDecimal());
        Assert.Equal(46.6753m, general.GetProperty("lon").GetDecimal());
    }

    [Fact]
    public async Task GeneralInfo_MissingWebsite_Returns422()
    {
        var id = await BuildApprovedEstablishmentWithMember("CR-PROF-VAL", ValidationUser.Sub);
        var client = _factory.CreateClientFor(ValidationUser);

        using var form = new MultipartFormDataContent
        {
            { new StringContent("A description."), "description" },
            { new StringContent("24.7"), "lat" },
            { new StringContent("46.6"), "lon" },
            { new StringContent("12"), "building_number" },
            { new StringContent("PATCH"), "_method" },
        };
        var response = await client.PostAsync($"/api/establishments/me/profile/general-info?establishment_id={id}", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("website", out _));
    }

    [Fact]
    public async Task GeneralInfo_Anonymous_Returns401()
    {
        var anon = _factory.CreateClientFor(null);
        using var form = new MultipartFormDataContent { { new StringContent("PATCH"), "_method" } };
        var response = await anon.PostAsync("/api/establishments/me/profile/general-info", form);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -- contact-info ---------------------------------------------------------

    [Fact]
    public async Task ContactInfo_Updates_AndGetReflects()
    {
        var id = await BuildApprovedEstablishmentWithMember("CR-PROF-CON", ContactUser.Sub);
        var client = _factory.CreateClientFor(ContactUser);

        var body = new
        {
            contact_number = "+966511111111",
            additional_contact_number = "+966522222222",
            email = "contact-edit@acme.test",
        };
        var response = await client.PatchAsJsonAsync(
            $"/api/establishments/me/profile/contact-info?establishment_id={id}", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await GetProfileAsync(client, id);
        var contact = data.GetProperty("profile").GetProperty("contact_info");
        Assert.Equal("+966511111111", contact.GetProperty("contact_number").GetString());
        Assert.Equal("+966522222222", contact.GetProperty("additional_contact_number").GetString());
        Assert.Equal("contact-edit@acme.test", contact.GetProperty("email").GetString());
        // Email also surfaces at the resource top level.
        Assert.Equal("contact-edit@acme.test", data.GetProperty("email").GetString());
    }

    [Fact]
    public async Task ContactInfo_Precognition_StopsBeforeMutation_204()
    {
        var id = await BuildApprovedEstablishmentWithMember("CR-PROF-PRE", PrecogUser.Sub);
        var client = _factory.CreateClientFor(PrecogUser);

        var req = new HttpRequestMessage(
            HttpMethod.Patch, $"/api/establishments/me/profile/contact-info?establishment_id={id}")
        {
            Content = JsonContent.Create(new
            {
                contact_number = "+966533333333",
                additional_contact_number = "+966544444444",
                email = "precog@acme.test",
            }),
        };
        req.Headers.Add("Precognition", "true");
        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Nothing changed — email is still the registration default.
        var contact = (await GetProfileAsync(client, id)).GetProperty("profile").GetProperty("contact_info");
        Assert.Equal("ops@acme.test", contact.GetProperty("email").GetString());
    }

    [Fact]
    public async Task ContactInfo_DuplicateEmail_Returns422()
    {
        // Two establishments exist; the registration default email (ops@acme.test)
        // is shared, so trying to set it explicitly trips the uniqueness rule.
        var id = await BuildApprovedEstablishmentWithMember("CR-PROF-DUP", DupUser.Sub);
        await BuildApprovedEstablishmentWithMember("CR-PROF-DUP-OTHER", DupUser.Sub);
        var client = _factory.CreateClientFor(DupUser);

        var body = new
        {
            contact_number = "+966555555555",
            additional_contact_number = "+966566666666",
            email = "ops@acme.test",
        };
        var response = await client.PatchAsJsonAsync(
            $"/api/establishments/me/profile/contact-info?establishment_id={id}", body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("email", out _));
    }

    // -- experience -----------------------------------------------------------

    [Fact]
    public async Task Experience_Updates_AndGetReflects()
    {
        var id = await BuildApprovedEstablishmentWithMember("CR-PROF-EXP", ExperienceUser.Sub);
        var client = _factory.CreateClientFor(ExperienceUser);

        var response = await client.PatchAsJsonAsync(
            $"/api/establishments/me/profile/experience?establishment_id={id}",
            new { years_of_experience = 8 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var general = (await GetProfileAsync(client, id)).GetProperty("profile").GetProperty("general_info");
        Assert.Equal(8, general.GetProperty("years_of_experience").GetInt32());
    }

    [Fact]
    public async Task Experience_OutOfRange_Returns422()
    {
        var id = await BuildApprovedEstablishmentWithMember("CR-PROF-EXP2", ExperienceUser.Sub);
        var client = _factory.CreateClientFor(ExperienceUser);

        var response = await client.PatchAsJsonAsync(
            $"/api/establishments/me/profile/experience?establishment_id={id}",
            new { years_of_experience = 0 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("years_of_experience", out _));
    }

    // -- helpers --------------------------------------------------------------

    private async Task<JsonElement> GetProfileAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/establishments/me/profile?establishment_id={id}");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.DataOf().Clone();
    }

    private async Task<Guid> BuildApprovedEstablishmentWithMember(string crNumber, string memberSub)
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);

        var id = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: crNumber);
        await creator.PostAsync($"/api/v1/establishments/registration/{id}/submit", content: null);
        await admin.PostAsync($"/api/v1/admin/establishments/{id}/approve", content: null);

        var addResp = await creator.PostAsJsonAsync(
            $"/api/v1/establishments/{id}/members",
            new { userId = memberSub, role = "Manager" });
        if (addResp.StatusCode != HttpStatusCode.Created
            && addResp.StatusCode != HttpStatusCode.Conflict)
        {
            addResp.EnsureSuccessStatusCode();
        }
        return id;
    }
}
