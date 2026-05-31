using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Profile;

/// <summary>
/// Phase-D integration tests for the seven profile mutator endpoints and the
/// per-id deletes (Features/Profile/). Each mutator mirrors a legacy Laravel
/// controller and MUST return the refreshed UserResource wrapped in a
/// <c>{ data }</c> envelope; the deletes return 204 (the frontend refetches).
///
/// All tests run against the InMemory host with fake auth (X-Test-User) — no
/// real IdM / Bearer token. The user row is auto-provisioned by
/// CurrentUserSyncMiddleware on the first authenticated request, so each test
/// just uses a distinct <c>sub</c> to stay isolated within the shared DB.
/// Reference lookups (city/region/bank/language/categories) are seeded once.
/// </summary>
public sealed class ProfileMutatorsTests
    : IClassFixture<ProfileMutatorsApiFactory>, IAsyncLifetime
{
    private readonly ProfileMutatorsApiFactory _factory;

    // Stable reference ids, seeded idempotently in InitializeAsync.
    private static readonly Guid CityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RegionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BankId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ArabicLanguageId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid EnglishLanguageId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ParentCategoryId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid ChildCategoryId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    // A real, mod-97-valid Saudi IBAN (SAMA reference example).
    private const string ValidIban = "SA0380000000608010167519";

    private const string PersonalInfoUrl = "/api/v1/users/profile/personal-info";
    private const string LanguagesSkillsUrl = "/api/v1/users/profile/languages-skills";
    private const string ExperiencesUrl = "/api/v1/users/profile/user-experiences";
    private const string EducationUrl = "/api/v1/users/profile/user-education";
    private const string CertificatesUrl = "/api/v1/users/profile/user-certificates";
    private const string InterestUrl = "/api/v1/users/profile/interest";
    private const string PhotoUrl = "/api/v1/users/profile/photo";
    private const string ProfileUrl = "/api/v1/profile";

    public ProfileMutatorsTests(ProfileMutatorsApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Cities.AnyAsync(c => c.Id == CityId)) return;

        db.Cities.Add(new City(CityId, "Riyadh"));
        db.Regions.Add(new Region(RegionId, "Riyadh Region"));
        db.Banks.Add(new Bank(BankId, "Al Rajhi Bank"));
        db.Languages.Add(new Language(ArabicLanguageId, "Arabic"));
        db.Languages.Add(new Language(EnglishLanguageId, "English"));
        db.OpportunityCategories.Add(new OpportunityCategory(ParentCategoryId, "Events"));
        db.OpportunityCategories.Add(new OpportunityCategory(
            ChildCategoryId, "Ushering", parentId: ParentCategoryId, forVacancy: true));
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---- shared helpers ---------------------------------------------------

    private HttpClient ClientFor(string tag)
        => _factory.CreateClientFor(new TestUser($"profile-mut-{tag}", new[] { "matloob_user" }));

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client, HttpMethod method, string url, object body, bool precognition = false)
    {
        var req = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        if (precognition) req.Headers.Add("Precognition", "true");
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> SendMultipartAsync(
        HttpClient client, string url, MultipartFormDataContent content, bool precognition = false)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (precognition) req.Headers.Add("Precognition", "true");
        return await client.SendAsync(req);
    }

    private static void AddField(MultipartFormDataContent form, string name, string value)
        => form.Add(new StringContent(value), name);

    private static void AddFile(
        MultipartFormDataContent form, string name, byte[] bytes, string contentType, string fileName)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, name, fileName);
    }

    private static readonly byte[] PngBytes =
        { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00 };

    /// <summary>Reads the <c>data</c> element from a mutator response and asserts 200.</summary>
    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage resp)
    {
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").Clone();
    }

    private static async Task<JsonElement> GetProfileDataAsync(HttpClient client)
    {
        var json = await client.GetStringAsync(ProfileUrl);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").Clone();
    }

    private static object ValidPersonalInfo() => new
    {
        name = "Mona Test",
        email = "mona@example.test",
        phone_number = "+966500000001",
        additional_phone_number = "+966500000002",
        bio = "Event professional.",
        city_id = CityId,
        region_id = RegionId,
        bank_id = BankId,
        iban = ValidIban,
    };

    // ======================================================================
    // personal-info
    // ======================================================================

    [Fact]
    public async Task PersonalInfo_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var resp = await SendJsonAsync(anon, HttpMethod.Patch, PersonalInfoUrl, ValidPersonalInfo());
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task PersonalInfo_ValidPayload_UpdatesUserAndBankAccount()
    {
        var client = ClientFor("pi-ok");

        var data = await ReadDataAsync(
            await SendJsonAsync(client, HttpMethod.Patch, PersonalInfoUrl, ValidPersonalInfo()));

        Assert.Equal("mona@example.test", data.GetProperty("email").GetString());
        Assert.Equal("Mona Test", data.GetProperty("name").GetString());
        Assert.Equal("+966500000001", data.GetProperty("phone_number").GetString());
        Assert.Equal("+966500000002", data.GetProperty("additional_phone_number").GetString());
        Assert.Equal("Event professional.", data.GetProperty("bio").GetString());
        Assert.Equal(CityId, data.GetProperty("city").GetProperty("id").GetGuid());
        Assert.Equal(RegionId, data.GetProperty("region").GetProperty("id").GetGuid());

        var bank = data.GetProperty("bank_account");
        Assert.Equal(JsonValueKind.Object, bank.ValueKind);
        Assert.Equal(ValidIban, bank.GetProperty("iban").GetString());
        Assert.Equal(BankId, bank.GetProperty("bank").GetProperty("id").GetGuid());

        // GET reflects the change.
        var fetched = await GetProfileDataAsync(client);
        Assert.Equal("mona@example.test", fetched.GetProperty("email").GetString());
        Assert.Equal(ValidIban, fetched.GetProperty("bank_account").GetProperty("iban").GetString());
    }

    [Fact]
    public async Task PersonalInfo_SecondCall_UpdatesExistingBankAccount()
    {
        var client = ClientFor("pi-upsert");
        await SendJsonAsync(client, HttpMethod.Patch, PersonalInfoUrl, ValidPersonalInfo());

        var second = (object)new
        {
            name = "Mona Updated",
            email = "mona2@example.test",
            phone_number = "+966500000003",
            additional_phone_number = "+966500000004",
            bio = "Updated bio.",
            city_id = CityId,
            region_id = RegionId,
            bank_id = BankId,
            iban = ValidIban,
        };
        var data = await ReadDataAsync(
            await SendJsonAsync(client, HttpMethod.Patch, PersonalInfoUrl, second));

        Assert.Equal("mona2@example.test", data.GetProperty("email").GetString());
        // Still exactly one bank account (upsert, not insert).
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = data.GetProperty("id").GetGuid();
        Assert.Equal(1, await db.BankAccounts.CountAsync(b => b.UserId == userId));
    }

    [Fact]
    public async Task PersonalInfo_InvalidIban_Returns422()
    {
        var client = ClientFor("pi-iban");
        var body = new
        {
            name = "Bad Iban",
            email = "bad@example.test",
            phone_number = "+966500000001",
            additional_phone_number = "+966500000002",
            bio = "Bio.",
            city_id = CityId,
            region_id = RegionId,
            bank_id = BankId,
            iban = "SA0000000000000000000000",
        };
        var resp = await SendJsonAsync(client, HttpMethod.Patch, PersonalInfoUrl, body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task PersonalInfo_UnknownCity_Returns422()
    {
        var client = ClientFor("pi-city");
        var body = new
        {
            name = "Bad City",
            email = "city@example.test",
            phone_number = "+966500000001",
            additional_phone_number = "+966500000002",
            bio = "Bio.",
            city_id = Guid.NewGuid(),
            region_id = RegionId,
            bank_id = BankId,
            iban = ValidIban,
        };
        var resp = await SendJsonAsync(client, HttpMethod.Patch, PersonalInfoUrl, body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task PersonalInfo_MissingRequiredFields_Returns422WithSnakeCaseErrors()
    {
        // FluentValidation failures now return Laravel-style 422 with a
        // { message, errors: { snake_case_field: [...] } } body (the frontend's
        // laravel-precognition client keys field errors by the submitted name).
        var client = ClientFor("pi-required");
        var resp = await SendJsonAsync(client, HttpMethod.Patch, PersonalInfoUrl, new { name = "" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.TryGetProperty("data", out _)); // not enveloped
        Assert.True(doc.RootElement.TryGetProperty("message", out _));
        var errors = doc.RootElement.GetProperty("errors");
        // snake_case keys matching the request payload (not PascalCase/camelCase).
        Assert.True(errors.TryGetProperty("phone_number", out _));
        Assert.True(errors.TryGetProperty("additional_phone_number", out _));
        Assert.True(errors.TryGetProperty("city_id", out _));
    }

    [Fact]
    public async Task PersonalInfo_Precognition_Returns204_WithoutMutating()
    {
        var client = ClientFor("pi-precog");
        var resp = await SendJsonAsync(
            client, HttpMethod.Patch, PersonalInfoUrl, ValidPersonalInfo(), precognition: true);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        // No mutation occurred — email is still unset.
        var data = await GetProfileDataAsync(client);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("bank_account").ValueKind);
        Assert.True(data.GetProperty("email").ValueKind is JsonValueKind.Null
                    || string.IsNullOrEmpty(data.GetProperty("email").GetString()));
    }

    // ======================================================================
    // languages-skills
    // ======================================================================

    [Fact]
    public async Task LanguagesSkills_ReplacesSkillsAndSyncsLanguages()
    {
        var client = ClientFor("ls-ok");
        var body = new
        {
            skills = new[]
            {
                new { name = "Crowd Management", level = "expert" },
                new { name = "First Aid", level = "intermediate" },
            },
            languages = new[]
            {
                new { id = ArabicLanguageId, level = "expert" },
                new { id = EnglishLanguageId, level = "intermediate" },
            },
        };

        var data = await ReadDataAsync(
            await SendJsonAsync(client, HttpMethod.Patch, LanguagesSkillsUrl, body));

        Assert.Equal(2, data.GetProperty("skills").GetArrayLength());
        Assert.Equal(2, data.GetProperty("languages").GetArrayLength());
        Assert.Contains(
            data.GetProperty("skills").EnumerateArray(),
            s => s.GetProperty("name").GetString() == "Crowd Management"
                 && s.GetProperty("level").GetString() == "expert");
    }

    [Fact]
    public async Task LanguagesSkills_SecondCall_FullyReplacesSkills()
    {
        var client = ClientFor("ls-replace");
        await SendJsonAsync(client, HttpMethod.Patch, LanguagesSkillsUrl, new
        {
            skills = new[] { new { name = "Old Skill", level = "beginner" } },
            languages = Array.Empty<object>(),
        });

        var data = await ReadDataAsync(await SendJsonAsync(client, HttpMethod.Patch, LanguagesSkillsUrl, new
        {
            skills = new[] { new { name = "New Skill", level = "expert" } },
            languages = Array.Empty<object>(),
        }));

        Assert.Equal(1, data.GetProperty("skills").GetArrayLength());
        Assert.Equal("New Skill", data.GetProperty("skills")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task LanguagesSkills_UnknownLanguage_Returns422()
    {
        var client = ClientFor("ls-badlang");
        var body = new
        {
            skills = Array.Empty<object>(),
            languages = new[] { new { id = Guid.NewGuid(), level = "expert" } },
        };
        var resp = await SendJsonAsync(client, HttpMethod.Patch, LanguagesSkillsUrl, body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ======================================================================
    // experiences
    // ======================================================================

    [Fact]
    public async Task Experiences_SetsYearsAndReplacesSet()
    {
        var client = ClientFor("exp-ok");
        var body = new
        {
            years_of_experience = 6,
            experiences = new[]
            {
                new
                {
                    company = "Acme Events",
                    position = "Coordinator",
                    from = "2019-01-01",
                    to = (string?)null,
                    current = true,
                    description = "Lead coordinator.",
                    type = "full_time",
                },
            },
        };

        var data = await ReadDataAsync(
            await SendJsonAsync(client, HttpMethod.Patch, ExperiencesUrl, body));

        Assert.Equal(6, data.GetProperty("years_of_experience").GetInt32());
        Assert.Equal(1, data.GetProperty("experiences").GetArrayLength());
        var exp = data.GetProperty("experiences")[0];
        Assert.Equal("Acme Events", exp.GetProperty("company").GetString());
        Assert.True(exp.GetProperty("current").GetBoolean());
        Assert.Equal("full_time", exp.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, exp.GetProperty("to").ValueKind);
    }

    [Fact]
    public async Task Experiences_EmptySet_ClearsExperiences()
    {
        var client = ClientFor("exp-clear");
        await SendJsonAsync(client, HttpMethod.Patch, ExperiencesUrl, new
        {
            years_of_experience = 3,
            experiences = new[]
            {
                new
                {
                    company = "Old Co", position = "Intern", from = "2018-01-01",
                    to = "2019-01-01", current = false, description = (string?)null, type = "internship",
                },
            },
        });

        var data = await ReadDataAsync(await SendJsonAsync(client, HttpMethod.Patch, ExperiencesUrl, new
        {
            years_of_experience = 4,
            experiences = Array.Empty<object>(),
        }));

        Assert.Equal(4, data.GetProperty("years_of_experience").GetInt32());
        Assert.Equal(0, data.GetProperty("experiences").GetArrayLength());
    }

    // ======================================================================
    // education (multipart, no file)
    // ======================================================================

    [Fact]
    public async Task Education_UpsertsEntry()
    {
        var client = ClientFor("edu-ok");
        var form = new MultipartFormDataContent();
        AddField(form, "education[0][degree]", "bachelor");
        AddField(form, "education[0][specialization]", "Computer Science");
        AddField(form, "education[0][gpa_system]", "4");
        AddField(form, "education[0][gpa]", "3.5");
        AddField(form, "education[0][graduation_year]", "2020");

        var data = await ReadDataAsync(await SendMultipartAsync(client, EducationUrl, form));

        Assert.Equal(1, data.GetProperty("education").GetArrayLength());
        var edu = data.GetProperty("education")[0];
        Assert.Equal("bachelor", edu.GetProperty("degree").GetString());
        Assert.Equal("Computer Science", edu.GetProperty("specialization").GetString());
        Assert.Equal(4, edu.GetProperty("gpa_system").GetInt32());
        Assert.Equal(2020, edu.GetProperty("graduation_year").GetInt32());
    }

    [Fact]
    public async Task Education_InvalidDegree_Returns422()
    {
        var client = ClientFor("edu-bad");
        var form = new MultipartFormDataContent();
        AddField(form, "education[0][degree]", "phd-not-a-degree");
        AddField(form, "education[0][gpa_system]", "4");
        AddField(form, "education[0][gpa]", "3.5");
        AddField(form, "education[0][graduation_year]", "2020");

        var resp = await SendMultipartAsync(client, EducationUrl, form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Education_GpaOutOfRange_Returns422()
    {
        var client = ClientFor("edu-gpa");
        var form = new MultipartFormDataContent();
        AddField(form, "education[0][degree]", "bachelor");
        AddField(form, "education[0][gpa_system]", "4");
        AddField(form, "education[0][gpa]", "9.9");
        AddField(form, "education[0][graduation_year]", "2020");

        var resp = await SendMultipartAsync(client, EducationUrl, form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ======================================================================
    // certificates (multipart, no file)
    // ======================================================================

    [Fact]
    public async Task Certificates_UpsertThenDeleteNotInSet()
    {
        var client = ClientFor("cert-ok");

        var form1 = new MultipartFormDataContent();
        AddField(form1, "certificates[0][name]", "Safety Level 1");
        AddField(form1, "certificates[0][issued_by]", "NEC Academy");
        AddField(form1, "certificates[0][issued_at]", "2021-06-01");
        var data1 = await ReadDataAsync(await SendMultipartAsync(client, CertificatesUrl, form1));
        Assert.Equal(1, data1.GetProperty("certificates").GetArrayLength());
        Assert.Equal("Safety Level 1", data1.GetProperty("certificates")[0].GetProperty("name").GetString());

        // No certificates in the payload deletes the not-in-set certificate.
        // (The frontend always submits at least the spoofed _method field, so the
        // multipart body is well-formed even when the array is absent.)
        var form2 = new MultipartFormDataContent();
        AddField(form2, "_method", "patch");
        var data2 = await ReadDataAsync(await SendMultipartAsync(client, CertificatesUrl, form2));
        Assert.Equal(0, data2.GetProperty("certificates").GetArrayLength());
    }

    [Fact]
    public async Task Certificates_InvalidDate_Returns422()
    {
        var client = ClientFor("cert-date");
        var form = new MultipartFormDataContent();
        AddField(form, "certificates[0][name]", "X");
        AddField(form, "certificates[0][issued_by]", "Y");
        AddField(form, "certificates[0][issued_at]", "not-a-date");

        var resp = await SendMultipartAsync(client, CertificatesUrl, form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ======================================================================
    // interest (multipart, no file)
    // ======================================================================

    [Fact]
    public async Task Interest_SyncsProfessions()
    {
        var client = ClientFor("int-ok");
        var form = new MultipartFormDataContent();
        AddField(form, "professions[0][id]", ChildCategoryId.ToString());

        var data = await ReadDataAsync(await SendMultipartAsync(client, InterestUrl, form));

        Assert.Equal(1, data.GetProperty("professions").GetArrayLength());
        Assert.Equal(ChildCategoryId, data.GetProperty("professions")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Interest_NoProfessions_Returns422()
    {
        var client = ClientFor("int-empty");
        var form = new MultipartFormDataContent();
        AddField(form, "_method", "patch");
        var resp = await SendMultipartAsync(client, InterestUrl, form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Interest_UnknownProfession_Returns422()
    {
        var client = ClientFor("int-bad");
        var form = new MultipartFormDataContent();
        AddField(form, "professions[0][id]", Guid.NewGuid().ToString());

        var resp = await SendMultipartAsync(client, InterestUrl, form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Interest_ParentCategoryRejected_Returns422()
    {
        // Only child categories (ParentId != null) are valid professions.
        var client = ClientFor("int-parent");
        var form = new MultipartFormDataContent();
        AddField(form, "professions[0][id]", ParentCategoryId.ToString());

        var resp = await SendMultipartAsync(client, InterestUrl, form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ======================================================================
    // photo (multipart with file)
    // ======================================================================

    [Fact]
    public async Task Photo_ValidImage_SetsPhotoAsset()
    {
        var client = ClientFor("photo-ok");
        var form = new MultipartFormDataContent();
        AddFile(form, "photo", PngBytes, "image/png", "avatar.png");

        var data = await ReadDataAsync(await SendMultipartAsync(client, PhotoUrl, form));

        var photo = data.GetProperty("photo").GetString();
        Assert.False(string.IsNullOrEmpty(photo));
        Assert.Contains("/api/v1/assets/", photo);
    }

    [Fact]
    public async Task Photo_MissingFile_Returns422()
    {
        var client = ClientFor("photo-none");
        var form = new MultipartFormDataContent();
        AddField(form, "_method", "patch");
        var resp = await SendMultipartAsync(client, PhotoUrl, form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Photo_WrongContentType_Returns422()
    {
        var client = ClientFor("photo-type");
        var form = new MultipartFormDataContent();
        AddFile(form, "photo", PngBytes, "text/plain", "notimage.txt");

        var resp = await SendMultipartAsync(client, PhotoUrl, form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ======================================================================
    // per-id deletes
    // ======================================================================

    [Fact]
    public async Task DeleteSkill_RemovesSkill_AndGetReflects()
    {
        var client = ClientFor("del-skill");
        var data = await ReadDataAsync(await SendJsonAsync(client, HttpMethod.Patch, LanguagesSkillsUrl, new
        {
            skills = new[] { new { name = "Deletable", level = "beginner" } },
            languages = Array.Empty<object>(),
        }));
        var skillId = data.GetProperty("skills")[0].GetProperty("id").GetGuid();

        var del = await client.DeleteAsync($"/api/v1/users/profile/user-skills/{skillId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var fetched = await GetProfileDataAsync(client);
        Assert.Equal(0, fetched.GetProperty("skills").GetArrayLength());
    }

    [Fact]
    public async Task DeleteExperience_RemovesExperience()
    {
        var client = ClientFor("del-exp");
        var data = await ReadDataAsync(await SendJsonAsync(client, HttpMethod.Patch, ExperiencesUrl, new
        {
            years_of_experience = 2,
            experiences = new[]
            {
                new
                {
                    company = "Co", position = "Role", from = "2020-01-01",
                    to = (string?)null, current = true, description = (string?)null, type = "full_time",
                },
            },
        }));
        var expId = data.GetProperty("experiences")[0].GetProperty("id").GetGuid();

        var del = await client.DeleteAsync($"/api/v1/users/profile/user-experiences/{expId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var fetched = await GetProfileDataAsync(client);
        Assert.Equal(0, fetched.GetProperty("experiences").GetArrayLength());
    }

    [Fact]
    public async Task DeleteCertificate_RemovesCertificate()
    {
        var client = ClientFor("del-cert");
        var form = new MultipartFormDataContent();
        AddField(form, "certificates[0][name]", "ToDelete");
        AddField(form, "certificates[0][issued_by]", "NEC");
        AddField(form, "certificates[0][issued_at]", "2022-01-01");
        var data = await ReadDataAsync(await SendMultipartAsync(client, CertificatesUrl, form));
        var certId = data.GetProperty("certificates")[0].GetProperty("id").GetGuid();

        var del = await client.DeleteAsync($"/api/v1/users/profile/user-certificates/{certId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var fetched = await GetProfileDataAsync(client);
        Assert.Equal(0, fetched.GetProperty("certificates").GetArrayLength());
    }

    [Fact]
    public async Task DeleteSkill_UnknownId_Returns404()
    {
        var client = ClientFor("del-404");
        var del = await client.DeleteAsync($"/api/v1/users/profile/user-skills/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, del.StatusCode);
    }

    [Fact]
    public async Task DeleteSkill_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var del = await anon.DeleteAsync($"/api/v1/users/profile/user-skills/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, del.StatusCode);
    }
}
