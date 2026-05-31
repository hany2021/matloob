using System.Net;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Common;
using Matloob.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Establishments;

/// <summary>
/// Integration tests for the establishment Events slice
/// (Features/Establishments/Events). Each test builds its own Approved
/// establishment (Creator → Owner) and passes <c>?establishment_id=</c>
/// explicitly. Reference data (event type, category, suggested location/
/// attendee) is seeded once. Responses ride the global { data } envelope.
/// </summary>
public sealed class EventsTests : IClassFixture<EstablishmentsApiFactory>
{
    private readonly EstablishmentsApiFactory _factory;

    private static readonly Guid EventTypeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid CategoryId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid LocationId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
    private static readonly Guid AttendeeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");

    public EventsTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    private async Task SeedRefAsync()
    {
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.EventTypes.AnyAsync(t => t.Id == EventTypeId)) return;

        db.EventTypes.Add(new EventType(EventTypeId, "Conference", "desc", "bg.png", "icon.png"));
        db.OpportunityCategories.Add(new OpportunityCategory(CategoryId, "Ushering", forVacancy: true));
        db.SuggestedLocations.Add(new SuggestedLocation(LocationId, "Riyadh Expo", 24.7m, 46.6m));
        db.SuggestedAttendees.Add(new SuggestedAttendee(AttendeeId, 100, 500));
        await db.SaveChangesAsync();
    }

    private async Task<Guid> BuildEstablishmentAsync(string cr)
    {
        await SeedRefAsync();
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);
        var id = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: cr);
        await creator.PostAsync($"/api/v1/establishments/registration/{id}/submit", content: null);
        (await admin.PostAsync($"/api/v1/admin/establishments/{id}/approve", content: null))
            .EnsureSuccessStatusCode();
        return id;
    }

    private HttpClient Owner() => _factory.CreateClientFor(Helpers.Creator);

    private static MultipartFormDataContent Form(params (string Key, string Value)[] fields)
    {
        var form = new MultipartFormDataContent();
        foreach (var (key, value) in fields)
            form.Add(new StringContent(value), key);
        return form;
    }

    private static (string, string)[] StepOne(string name = "Gala Night") =>
    [
        ("step_one[type_uuid]", EventTypeId.ToString()),
        ("step_one[name]", name),
        ("step_one[description]", "An evening celebration event."),
    ];

    private static (string, string)[] StepTwo(string start, string end) =>
    [
        ("step_two[lat]", "24.7"),
        ("step_two[lon]", "46.6"),
        ("step_two[location_title]", "Riyadh"),
        ("step_two[start_date]", start),
        ("step_two[end_date]", end),
        ("step_two[min_attendees]", "100"),
        ("step_two[max_attendees]", "500"),
    ];

    private static async Task<Guid> CreateDraftAsync(HttpClient owner, Guid est, string name = "Gala Night")
    {
        var resp = await owner.PostAsync(
            $"/api/establishments/events?establishment_id={est}", Form(StepOne(name)));
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.DataOf().GetProperty("id").GetGuid();
    }

    // ===================== reads / auth =====================

    [Fact]
    public async Task Events_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var resp = await anon.GetAsync("/api/establishments/events");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task EventTypes_ReturnsSeeded()
    {
        await SeedRefAsync();
        var resp = await Owner().GetAsync("/api/establishments/events/types");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Contains(doc.RootElement.DataOf().EnumerateArray(),
            t => t.GetProperty("id").GetGuid() == EventTypeId);
    }

    [Fact]
    public async Task SuggestedLocationsAndAttendees_ReturnSeeded()
    {
        await SeedRefAsync();
        var owner = Owner();

        using (var locDoc = JsonDocument.Parse(
            await (await owner.GetAsync("/api/establishments/events/suggested-locations"))
                .Content.ReadAsStringAsync()))
        {
            Assert.Contains(locDoc.RootElement.DataOf().EnumerateArray(),
                l => l.GetProperty("id").GetGuid() == LocationId);
        }

        using var attDoc = JsonDocument.Parse(
            await (await owner.GetAsync("/api/establishments/events/suggested-attendees"))
                .Content.ReadAsStringAsync());
        Assert.Contains(attDoc.RootElement.DataOf().EnumerateArray(),
            a => a.GetProperty("min").GetInt32() == 100 && a.GetProperty("max").GetInt32() == 500);
    }

    // ===================== create / update =====================

    [Fact]
    public async Task CreateEvent_StepOne_Returns201_Draft()
    {
        var est = await BuildEstablishmentAsync("CR-EV-CREATE");
        var resp = await Owner().PostAsync(
            $"/api/establishments/events?establishment_id={est}", Form(StepOne("My Conference")));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var data = doc.RootElement.DataOf();
        Assert.Equal("My Conference", data.GetProperty("name").GetString());
        Assert.Equal("drafted", data.GetProperty("status").GetString());
        Assert.Equal(EventTypeId, data.GetProperty("type").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task CreateEvent_MissingType_Returns422()
    {
        var est = await BuildEstablishmentAsync("CR-EV-422");
        var resp = await Owner().PostAsync(
            $"/api/establishments/events?establishment_id={est}",
            Form(("step_one[name]", "No Type"), ("step_one[description]", "desc here")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateEvent_StepTwo_AppliesSchedule()
    {
        var est = await BuildEstablishmentAsync("CR-EV-STEP2");
        var owner = Owner();
        var id = await CreateDraftAsync(owner, est);

        var resp = await owner.PatchAsync(
            $"/api/establishments/events/{id}?establishment_id={est}",
            Form(StepTwo("2026-12-01", "2026-12-05")));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var data = doc.RootElement.DataOf();
        Assert.Equal("2026-12-01", data.GetProperty("start_date").GetString());
        Assert.Equal(100, data.GetProperty("min_attendees").GetInt32());
        Assert.True(data.GetProperty("steps_done").GetInt32() >= 2);
    }

    [Fact]
    public async Task UpdateEvent_StepFour_SetsCategories()
    {
        var est = await BuildEstablishmentAsync("CR-EV-STEP4");
        var owner = Owner();
        var id = await CreateDraftAsync(owner, est);

        var resp = await owner.PatchAsync(
            $"/api/establishments/events/{id}?establishment_id={est}",
            Form(("step_four[opportunities_categories][]", CategoryId.ToString())));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var cats = doc.RootElement.DataOf().GetProperty("opportunity_categories");
        Assert.Equal(1, cats.GetArrayLength());
        Assert.Equal(CategoryId, cats[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task CreateEvent_WithPublish_FutureStart_IsUpcoming()
    {
        var est = await BuildEstablishmentAsync("CR-EV-PUB");
        var fields = new List<(string, string)>();
        fields.AddRange(StepOne("Future Fest"));
        fields.AddRange(StepTwo("2099-01-01", "2099-01-05"));
        fields.Add(("step_five[publish]", "1"));

        var resp = await Owner().PostAsync(
            $"/api/establishments/events?establishment_id={est}", Form(fields.ToArray()));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("upcoming", doc.RootElement.DataOf().GetProperty("status").GetString());
    }

    // ===================== grouped list / get =====================

    [Fact]
    public async Task ListEvents_GroupsByStatus()
    {
        var est = await BuildEstablishmentAsync("CR-EV-LIST");
        var owner = Owner();
        await CreateDraftAsync(owner, est, "Draft One");

        var pubFields = new List<(string, string)>();
        pubFields.AddRange(StepOne("Upcoming One"));
        pubFields.AddRange(StepTwo("2099-02-01", "2099-02-05"));
        pubFields.Add(("step_five[publish]", "1"));
        await owner.PostAsync($"/api/establishments/events?establishment_id={est}", Form(pubFields.ToArray()));

        var resp = await owner.GetAsync($"/api/establishments/events?establishment_id={est}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var data = doc.RootElement.DataOf();
        Assert.Equal(1, data.GetProperty("drafted").GetArrayLength());
        Assert.Equal(1, data.GetProperty("upcoming").GetArrayLength());
    }

    [Fact]
    public async Task GetEvent_ReturnsOne_AndUnknownIs404()
    {
        var est = await BuildEstablishmentAsync("CR-EV-GET");
        var owner = Owner();
        var id = await CreateDraftAsync(owner, est, "Findable");

        var get = await owner.GetAsync($"/api/establishments/events/{id}?establishment_id={est}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using (var gdoc = JsonDocument.Parse(await get.Content.ReadAsStringAsync()))
            Assert.Equal("Findable", gdoc.RootElement.DataOf().GetProperty("name").GetString());

        var unknown = await owner.GetAsync(
            $"/api/establishments/events/{Guid.NewGuid()}?establishment_id={est}");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    // ===================== end / delete / joined =====================

    [Fact]
    public async Task EndEvent_FlipsToEnded_AndDraftCannotEnd()
    {
        var est = await BuildEstablishmentAsync("CR-EV-END");
        var owner = Owner();

        // A published (upcoming) event can be ended.
        var pubFields = new List<(string, string)>();
        pubFields.AddRange(StepOne("Endable"));
        pubFields.AddRange(StepTwo("2099-03-01", "2099-03-05"));
        pubFields.Add(("step_five[publish]", "1"));
        var created = await owner.PostAsync(
            $"/api/establishments/events?establishment_id={est}", Form(pubFields.ToArray()));
        Guid pubId;
        using (var cdoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
            pubId = cdoc.RootElement.DataOf().GetProperty("id").GetGuid();

        var end = await owner.PatchAsync(
            $"/api/establishments/events/{pubId}/end?establishment_id={est}", content: null);
        Assert.Equal(HttpStatusCode.OK, end.StatusCode);
        using (var edoc = JsonDocument.Parse(await end.Content.ReadAsStringAsync()))
            Assert.Equal("ended", edoc.RootElement.DataOf().GetProperty("status").GetString());

        // A draft cannot be ended.
        var draftId = await CreateDraftAsync(owner, est, "Still Draft");
        var endDraft = await owner.PatchAsync(
            $"/api/establishments/events/{draftId}/end?establishment_id={est}", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, endDraft.StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_RemovesIt()
    {
        var est = await BuildEstablishmentAsync("CR-EV-DEL");
        var owner = Owner();
        var id = await CreateDraftAsync(owner, est, "Temp Event");

        var del = await owner.DeleteAsync($"/api/establishments/events/{id}?establishment_id={est}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var get = await owner.GetAsync($"/api/establishments/events/{id}?establishment_id={est}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task UpdateEvent_WithUploads_StoresAndReturnsThem()
    {
        var est = await BuildEstablishmentAsync("CR-EV-UPLOAD");
        var owner = Owner();
        var id = await CreateDraftAsync(owner, est, "With Files");

        var form = new MultipartFormDataContent();
        foreach (var (k, v) in StepTwo("2099-04-01", "2099-04-05")) form.Add(new StringContent(v), k);
        var png = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        png.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(png, "step_two[uploads][]", "cover.png");

        var resp = await owner.PatchAsync(
            $"/api/establishments/events/{id}?establishment_id={est}", form);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var uploads = doc.RootElement.DataOf().GetProperty("uploads");
        Assert.Equal(1, uploads.GetArrayLength());
        Assert.Equal("cover.png", uploads[0].GetProperty("name").GetString());
        Assert.Contains("/api/v1/assets/", uploads[0].GetProperty("url").GetString());

        // GET reflects the stored upload.
        using var getDoc = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/establishments/events/{id}?establishment_id={est}"))
                .Content.ReadAsStringAsync());
        Assert.Equal(1, getDoc.RootElement.DataOf().GetProperty("uploads").GetArrayLength());
    }

    [Fact]
    public async Task UpdateEvent_UploadWrongType_Returns422()
    {
        var est = await BuildEstablishmentAsync("CR-EV-UPLOAD-422");
        var owner = Owner();
        var id = await CreateDraftAsync(owner, est, "Bad File");

        var form = new MultipartFormDataContent();
        foreach (var (k, v) in StepTwo("2099-05-01", "2099-05-05")) form.Add(new StringContent(v), k);
        var txt = new ByteArrayContent(new byte[] { 1, 2, 3 });
        txt.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(txt, "step_two[uploads][]", "notes.txt");

        var resp = await owner.PatchAsync(
            $"/api/establishments/events/{id}?establishment_id={est}", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task JoinedEvents_IncludesCreatedEvent()
    {
        var est = await BuildEstablishmentAsync("CR-EV-JOINED");
        var owner = Owner();
        var id = await CreateDraftAsync(owner, est, "Mine Created");

        var resp = await owner.GetAsync($"/api/establishments/events/joined-events?establishment_id={est}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Contains(doc.RootElement.DataOf().EnumerateArray(),
            e => e.GetProperty("id").GetGuid() == id);
    }

    // ===================== drafted (open in editable form) =====================

    private async Task<JsonElement> GetDraftedAsync(HttpClient owner, Guid est, Guid id)
    {
        var resp = await owner.GetAsync($"/api/establishments/events/{id}/drafted?establishment_id={est}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.DataOf().Clone();
    }

    [Fact]
    public async Task Drafted_StepOneOnly_ReturnsStepOne_OmitsLaterSteps()
    {
        var est = await BuildEstablishmentAsync("CR-EV-DRAFT1");
        var owner = Owner();
        var id = await CreateDraftAsync(owner, est, "Draft Wizard");

        var data = await GetDraftedAsync(owner, est, id);
        Assert.Equal(id, data.GetProperty("id").GetGuid());
        Assert.Equal(1, data.GetProperty("steps_done").GetInt32());
        Assert.Equal(0, data.GetProperty("opportunities").GetArrayLength());

        var s1 = data.GetProperty("step_one");
        Assert.Equal(EventTypeId, s1.GetProperty("type_uuid").GetGuid());
        Assert.Equal("Draft Wizard", s1.GetProperty("name").GetString());

        // Steps the draft hasn't reached are omitted (progressive payload).
        Assert.False(data.TryGetProperty("step_two", out _));
        Assert.False(data.TryGetProperty("step_three", out _));
        Assert.False(data.TryGetProperty("step_four", out _));
    }

    [Fact]
    public async Task Drafted_AfterStepTwo_IncludesScheduleAndEmptyUploads()
    {
        var est = await BuildEstablishmentAsync("CR-EV-DRAFT2");
        var owner = Owner();
        var id = await CreateDraftAsync(owner, est);
        (await owner.PatchAsync(
            $"/api/establishments/events/{id}?establishment_id={est}",
            Form(StepTwo("2026-12-01", "2026-12-05")))).EnsureSuccessStatusCode();

        var data = await GetDraftedAsync(owner, est, id);
        Assert.True(data.TryGetProperty("step_one", out _));
        var s2 = data.GetProperty("step_two");
        Assert.Equal("2026-12-01", s2.GetProperty("start_date").GetString());
        Assert.Equal(100, s2.GetProperty("min_attendees").GetInt32());
        Assert.Equal(24.7m, s2.GetProperty("lat").GetDecimal());
        Assert.Equal(0, s2.GetProperty("uploads").GetArrayLength());
        Assert.False(data.TryGetProperty("step_four", out _));
    }

    [Fact]
    public async Task Drafted_AfterStepFour_IncludesCategories_AndEmptyStepThree()
    {
        var est = await BuildEstablishmentAsync("CR-EV-DRAFT4");
        var owner = Owner();
        var id = await CreateDraftAsync(owner, est);
        (await owner.PatchAsync(
            $"/api/establishments/events/{id}?establishment_id={est}",
            Form(("step_four[opportunities_categories][]", CategoryId.ToString())))).EnsureSuccessStatusCode();

        var data = await GetDraftedAsync(owner, est, id);
        Assert.Equal(4, data.GetProperty("steps_done").GetInt32());

        var cats = data.GetProperty("step_four").GetProperty("opportunities_categories");
        Assert.Equal(1, cats.GetArrayLength());
        Assert.Equal(CategoryId, cats[0].GetGuid());

        // step_three exists but success criteria are not backed yet -> [].
        Assert.Equal(0, data.GetProperty("step_three").GetProperty("success_criteria").GetArrayLength());
        Assert.Equal(0, data.GetProperty("opportunities").GetArrayLength());
    }

    [Fact]
    public async Task Drafted_WithUploads_ProjectsStepTwoUploads()
    {
        var est = await BuildEstablishmentAsync("CR-EV-DRAFT-UP");
        var owner = Owner();
        var id = await CreateDraftAsync(owner, est);

        var form = new MultipartFormDataContent();
        foreach (var (k, v) in StepTwo("2099-04-01", "2099-04-05")) form.Add(new StringContent(v), k);
        var png = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        png.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(png, "step_two[uploads][]", "cover.png");
        (await owner.PatchAsync(
            $"/api/establishments/events/{id}?establishment_id={est}", form)).EnsureSuccessStatusCode();

        var uploads = (await GetDraftedAsync(owner, est, id)).GetProperty("step_two").GetProperty("uploads");
        Assert.Equal(1, uploads.GetArrayLength());
        Assert.Equal("cover.png", uploads[0].GetProperty("name").GetString());
        Assert.Contains("/api/v1/assets/", uploads[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Drafted_Unknown_Returns404_AndAnonymous401()
    {
        var est = await BuildEstablishmentAsync("CR-EV-DRAFT-404");
        var owner = Owner();

        var unknown = await owner.GetAsync(
            $"/api/establishments/events/{Guid.NewGuid()}/drafted?establishment_id={est}");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var anon = _factory.CreateClientFor(null);
        var anonResp = await anon.GetAsync($"/api/establishments/events/{Guid.NewGuid()}/drafted");
        Assert.Equal(HttpStatusCode.Unauthorized, anonResp.StatusCode);
    }

    // ===================== step-three success criteria =====================

    private const string CritOutput = "Event ran smoothly";
    private const string CritSuccess = "All planned activities were completed on schedule and on budget.";
    private const string CritComment = "Measured against the agreed event success checklist and KPIs.";

    private static void AddCriterion(
        MultipartFormDataContent form, int idx, string output, string success, string? comment)
    {
        form.Add(new StringContent(output), $"step_three[success_criteria][{idx}][output]");
        form.Add(new StringContent(success), $"step_three[success_criteria][{idx}][success_criteria]");
        if (comment is not null)
            form.Add(new StringContent(comment), $"step_three[success_criteria][{idx}][comment]");
    }

    [Fact]
    public async Task Event_StepThree_CreatesCriteria_AndReadsReflect()
    {
        var est = await BuildEstablishmentAsync("CR-EV-SC1");
        var owner = Owner();
        var id = await CreateDraftAsync(owner, est);

        var form = new MultipartFormDataContent();
        AddCriterion(form, 0, CritOutput, CritSuccess, CritComment);
        var patch = await owner.PatchAsync($"/api/establishments/events/{id}?establishment_id={est}", form);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        // GET event reflects success_criteria.
        using (var gdoc = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/establishments/events/{id}?establishment_id={est}"))
                .Content.ReadAsStringAsync()))
        {
            var sc = gdoc.RootElement.DataOf().GetProperty("success_criteria");
            Assert.Equal(1, sc.GetArrayLength());
            Assert.Equal(CritOutput, sc[0].GetProperty("output").GetString());
            Assert.Equal(CritComment, sc[0].GetProperty("comment").GetString());
            Assert.Equal(0, sc[0].GetProperty("uploads").GetArrayLength());
        }

        // drafted read reflects step_three.success_criteria.
        var drafted = await GetDraftedAsync(owner, est, id);
        Assert.Equal(1, drafted.GetProperty("step_three").GetProperty("success_criteria").GetArrayLength());
    }

    [Fact]
    public async Task Event_StepThree_WithUpload_ProjectsCriterionUpload()
    {
        var est = await BuildEstablishmentAsync("CR-EV-SC-UP");
        var owner = Owner();
        var id = await CreateDraftAsync(owner, est);

        var form = new MultipartFormDataContent();
        AddCriterion(form, 0, CritOutput, CritSuccess, null);
        var png = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        png.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(png, "step_three[success_criteria][0][uploads][0]", "proof.png");

        (await owner.PatchAsync($"/api/establishments/events/{id}?establishment_id={est}", form))
            .EnsureSuccessStatusCode();

        using var gdoc = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/establishments/events/{id}?establishment_id={est}"))
                .Content.ReadAsStringAsync());
        var uploads = gdoc.RootElement.DataOf().GetProperty("success_criteria")[0].GetProperty("uploads");
        Assert.Equal(1, uploads.GetArrayLength());
        Assert.Equal("proof.png", uploads[0].GetProperty("name").GetString());
        Assert.Contains("/api/v1/assets/", uploads[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Event_StepThree_ShortOutput_Returns422()
    {
        var est = await BuildEstablishmentAsync("CR-EV-SC-422");
        var owner = Owner();
        var id = await CreateDraftAsync(owner, est);

        var form = new MultipartFormDataContent();
        AddCriterion(form, 0, "short", CritSuccess, null); // output < 10 chars
        var patch = await owner.PatchAsync($"/api/establishments/events/{id}?establishment_id={est}", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, patch.StatusCode);
    }

    [Fact]
    public async Task Event_StepThree_Replaces_Previous()
    {
        var est = await BuildEstablishmentAsync("CR-EV-SC-REP");
        var owner = Owner();
        var id = await CreateDraftAsync(owner, est);

        var form1 = new MultipartFormDataContent();
        AddCriterion(form1, 0, CritOutput, CritSuccess, null);
        AddCriterion(form1, 1, "Second outcome ok", CritSuccess, null);
        (await owner.PatchAsync($"/api/establishments/events/{id}?establishment_id={est}", form1))
            .EnsureSuccessStatusCode();

        var form2 = new MultipartFormDataContent();
        AddCriterion(form2, 0, "Only one remains", CritSuccess, null);
        (await owner.PatchAsync($"/api/establishments/events/{id}?establishment_id={est}", form2))
            .EnsureSuccessStatusCode();

        using var gdoc = JsonDocument.Parse(
            await (await owner.GetAsync($"/api/establishments/events/{id}?establishment_id={est}"))
                .Content.ReadAsStringAsync());
        var sc = gdoc.RootElement.DataOf().GetProperty("success_criteria");
        Assert.Equal(1, sc.GetArrayLength());
        Assert.Equal("Only one remains", sc[0].GetProperty("output").GetString());
    }
}
