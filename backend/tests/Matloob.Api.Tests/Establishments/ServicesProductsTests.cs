using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Matloob.Api.Tests.Common;

namespace Matloob.Api.Tests.Establishments;

/// <summary>
/// Integration tests for the establishment Services &amp; Products slice
/// (Features/Establishments/{Services,Products}). Each test builds its own
/// Approved establishment (Creator becomes an Owner member on approval) and
/// passes <c>?establishment_id=</c> explicitly, since Creator owns several
/// establishments across the shared fixture DB (auto-resolve would be
/// ambiguous). Responses are wrapped by the global { data } envelope.
/// </summary>
public sealed class ServicesProductsTests : IClassFixture<EstablishmentsApiFactory>
{
    private readonly EstablishmentsApiFactory _factory;

    public ServicesProductsTests(EstablishmentsApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<Guid> BuildEstablishmentAsync(string cr)
    {
        var creator = _factory.CreateClientFor(Helpers.Creator);
        var admin = _factory.CreateClientFor(Helpers.Admin);
        var id = await Helpers.CreateReadyToSubmitDraftAsync(
            _factory, creator, commercialRegistrationNumber: cr);
        await creator.PostAsync($"/api/v1/establishments/registration/{id}/submit", content: null);
        var approve = await admin.PostAsync($"/api/v1/admin/establishments/{id}/approve", content: null);
        approve.EnsureSuccessStatusCode();
        return id;
    }

    private HttpClient Owner() => _factory.CreateClientFor(Helpers.Creator);

    private static MultipartFormDataContent ProductForm(string name, string description)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "name");
        form.Add(new StringContent(description), "description");
        return form;
    }

    // ===================== services =====================

    [Fact]
    public async Task Services_Anonymous_ReturnsUnauthorized()
    {
        var anon = _factory.CreateClientFor(null);
        var resp = await anon.GetAsync("/api/establishments/me/services");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task CreateService_Returns201_WithEnvelopedData()
    {
        var est = await BuildEstablishmentAsync("CR-SVC-CREATE");
        var resp = await Owner().PostAsJsonAsync(
            $"/api/establishments/me/services?establishment_id={est}",
            new { name = "Catering", description = "Event catering services." });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var data = doc.RootElement.DataOf();
        Assert.Equal("Catering", data.GetProperty("name").GetString());
        Assert.Equal("Event catering services.", data.GetProperty("description").GetString());
        Assert.NotEqual(Guid.Empty, data.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task ListServices_ReturnsCreated_AsEnvelopedArray()
    {
        var est = await BuildEstablishmentAsync("CR-SVC-LIST");
        var owner = Owner();
        await owner.PostAsJsonAsync($"/api/establishments/me/services?establishment_id={est}",
            new { name = "Svc A", description = "desc a" });
        await owner.PostAsJsonAsync($"/api/establishments/me/services?establishment_id={est}",
            new { name = "Svc B", description = "desc b" });

        var resp = await owner.GetAsync($"/api/establishments/me/services?establishment_id={est}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var arr = doc.RootElement.DataOf();
        Assert.Equal(JsonValueKind.Array, arr.ValueKind);
        Assert.Equal(2, arr.GetArrayLength());
    }

    [Fact]
    public async Task GetService_ReturnsOne_AndUnknownIs404()
    {
        var est = await BuildEstablishmentAsync("CR-SVC-GET");
        var owner = Owner();
        var created = await owner.PostAsJsonAsync(
            $"/api/establishments/me/services?establishment_id={est}",
            new { name = "Lighting", description = "Stage lighting." });
        Guid id;
        using (var cdoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
            id = cdoc.RootElement.DataOf().GetProperty("id").GetGuid();

        var get = await owner.GetAsync($"/api/establishments/me/services/{id}?establishment_id={est}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using (var gdoc = JsonDocument.Parse(await get.Content.ReadAsStringAsync()))
            Assert.Equal("Lighting", gdoc.RootElement.DataOf().GetProperty("name").GetString());

        var unknown = await owner.GetAsync(
            $"/api/establishments/me/services/{Guid.NewGuid()}?establishment_id={est}");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task UpdateService_PatchesFields()
    {
        var est = await BuildEstablishmentAsync("CR-SVC-UPD");
        var owner = Owner();
        var created = await owner.PostAsJsonAsync(
            $"/api/establishments/me/services?establishment_id={est}",
            new { name = "Old", description = "old desc" });
        Guid id;
        using (var cdoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
            id = cdoc.RootElement.DataOf().GetProperty("id").GetGuid();

        var patch = await owner.PatchAsJsonAsync(
            $"/api/establishments/me/services/{id}?establishment_id={est}",
            new { name = "New", description = "new desc" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        using var doc = JsonDocument.Parse(await patch.Content.ReadAsStringAsync());
        Assert.Equal("New", doc.RootElement.DataOf().GetProperty("name").GetString());
    }

    [Fact]
    public async Task DeleteService_RemovesIt()
    {
        var est = await BuildEstablishmentAsync("CR-SVC-DEL");
        var owner = Owner();
        var created = await owner.PostAsJsonAsync(
            $"/api/establishments/me/services?establishment_id={est}",
            new { name = "Temp", description = "temp" });
        Guid id;
        using (var cdoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
            id = cdoc.RootElement.DataOf().GetProperty("id").GetGuid();

        var del = await owner.DeleteAsync($"/api/establishments/me/services/{id}?establishment_id={est}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var get = await owner.GetAsync($"/api/establishments/me/services/{id}?establishment_id={est}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task CreateService_EmptyName_Returns422()
    {
        var est = await BuildEstablishmentAsync("CR-SVC-422");
        var resp = await Owner().PostAsJsonAsync(
            $"/api/establishments/me/services?establishment_id={est}",
            new { name = "", description = "x" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task CreateService_WhenSuspended_Returns423()
    {
        var est = await BuildEstablishmentAsync("CR-SVC-SUSP");
        var admin = _factory.CreateClientFor(Helpers.Admin);
        var suspend = await admin.PostAsJsonAsync(
            $"/api/v1/admin/establishments/{est}/suspend", new { reason = "policy review" });
        suspend.EnsureSuccessStatusCode();

        var resp = await Owner().PostAsJsonAsync(
            $"/api/establishments/me/services?establishment_id={est}",
            new { name = "Blocked", description = "should fail" });
        Assert.Equal(HttpStatusCode.Locked, resp.StatusCode);
    }

    // ===================== products (multipart) =====================

    [Fact]
    public async Task CreateProduct_Multipart_Returns201_WithEnvelopedData()
    {
        var est = await BuildEstablishmentAsync("CR-PRD-CREATE");
        var resp = await Owner().PostAsync(
            $"/api/establishments/me/products?establishment_id={est}",
            ProductForm("T-Shirt", "Cotton event merch."));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var data = doc.RootElement.DataOf();
        Assert.Equal("T-Shirt", data.GetProperty("name").GetString());
        Assert.Equal("Cotton event merch.", data.GetProperty("description").GetString());
    }

    [Fact]
    public async Task ListProducts_ReturnsCreated()
    {
        var est = await BuildEstablishmentAsync("CR-PRD-LIST");
        var owner = Owner();
        await owner.PostAsync($"/api/establishments/me/products?establishment_id={est}",
            ProductForm("P1", "d1"));
        await owner.PostAsync($"/api/establishments/me/products?establishment_id={est}",
            ProductForm("P2", "d2"));

        var resp = await owner.GetAsync($"/api/establishments/me/products?establishment_id={est}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(2, doc.RootElement.DataOf().GetArrayLength());
    }

    [Fact]
    public async Task UpdateAndDeleteProduct_Work()
    {
        var est = await BuildEstablishmentAsync("CR-PRD-UPDDEL");
        var owner = Owner();
        var created = await owner.PostAsync(
            $"/api/establishments/me/products?establishment_id={est}",
            ProductForm("Mug", "ceramic"));
        Guid id;
        using (var cdoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
            id = cdoc.RootElement.DataOf().GetProperty("id").GetGuid();

        var patch = await owner.PatchAsync(
            $"/api/establishments/me/products/{id}?establishment_id={est}",
            ProductForm("Mug XL", "bigger ceramic"));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        using (var pdoc = JsonDocument.Parse(await patch.Content.ReadAsStringAsync()))
            Assert.Equal("Mug XL", pdoc.RootElement.DataOf().GetProperty("name").GetString());

        var del = await owner.DeleteAsync($"/api/establishments/me/products/{id}?establishment_id={est}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var get = await owner.GetAsync($"/api/establishments/me/products/{id}?establishment_id={est}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_MissingName_Returns422()
    {
        var est = await BuildEstablishmentAsync("CR-PRD-422");
        var resp = await Owner().PostAsync(
            $"/api/establishments/me/products?establishment_id={est}",
            ProductForm("", "no name"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ===================== profile wiring =====================

    [Fact]
    public async Task MeProfile_IncludesServicesAndProducts()
    {
        var est = await BuildEstablishmentAsync("CR-SP-PROFILE");
        var owner = Owner();
        await owner.PostAsJsonAsync($"/api/establishments/me/services?establishment_id={est}",
            new { name = "Audio", description = "sound" });
        await owner.PostAsync($"/api/establishments/me/products?establishment_id={est}",
            ProductForm("Banner", "printed"));

        var resp = await owner.GetAsync($"/api/establishments/me/profile?establishment_id={est}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var profile = doc.RootElement.DataOf().GetProperty("profile");

        var services = profile.GetProperty("services");
        var products = profile.GetProperty("products");
        Assert.Equal(1, services.GetArrayLength());
        Assert.Equal("Audio", services[0].GetProperty("name").GetString());
        Assert.Equal(1, products.GetArrayLength());
        Assert.Equal("Banner", products[0].GetProperty("name").GetString());
    }
}
