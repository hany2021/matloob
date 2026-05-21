using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Matloob.Api.Tests.Auth;

namespace Matloob.Api.Tests.Assets;

/// <summary>
/// Integration tests for the Assets API. Cover upload (anonymous + auth),
/// validation (size + content type), metadata read-back, download round-trip,
/// soft-delete, and the public/private visibility split.
///
/// All tests share one <see cref="AssetsApiFactory"/> so the in-memory
/// database accumulates rows across tests — that's fine because each test
/// owns a fresh asset id.
/// </summary>
public sealed class AssetsEndpointTests : IClassFixture<AssetsApiFactory>
{
    private readonly AssetsApiFactory _factory;

    public AssetsEndpointTests(AssetsApiFactory factory)
    {
        _factory = factory;
    }

    // -- helpers --------------------------------------------------------------

    private static readonly TestUser Uploader = new(
        Sub: "uploader-1",
        Roles: new[] { "matloob_user" });

    private static readonly TestUser OtherUser = new(
        Sub: "other-user",
        Roles: new[] { "matloob_user" });

    private static readonly TestUser Admin = new(
        Sub: "admin-1",
        Roles: new[] { "matloob_admin" });

    private HttpClient AnonClient() => _factory.CreateClientFor(null);
    private HttpClient ClientFor(TestUser u) => _factory.CreateClientFor(u);

    private static MultipartFormDataContent FileContent(
        byte[] bytes,
        string fileName,
        string contentType,
        string purpose = "Generic",
        string visibility = "Private")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "File", fileName);
        form.Add(new StringContent(purpose), "Purpose");
        form.Add(new StringContent(visibility), "Visibility");
        return form;
    }

    /// <summary>Minimal-but-valid PDF magic bytes.</summary>
    private static readonly byte[] SamplePdf =
        Encoding.ASCII.GetBytes("%PDF-1.4\n%fake-pdf-for-tests\n");

    private async Task<Guid> UploadAsAsync(
        TestUser user,
        string visibility = "Private",
        string purpose = "Generic")
    {
        using var client = ClientFor(user);
        using var form = FileContent(SamplePdf, "doc.pdf", "application/pdf", purpose, visibility);
        var response = await client.PostAsync("/api/v1/assets", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    // -- upload ---------------------------------------------------------------

    [Fact]
    public async Task Upload_WithoutUser_ReturnsUnauthorized()
    {
        using var client = AnonClient();
        using var form = FileContent(SamplePdf, "doc.pdf", "application/pdf");

        var response = await client.PostAsync("/api/v1/assets", form);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WithValidFile_ReturnsCreatedAndGuid()
    {
        using var client = ClientFor(Uploader);
        using var form = FileContent(
            SamplePdf, "Authorization Letter.pdf", "application/pdf",
            purpose: "AuthorizationLetter",
            visibility: "Private");

        var response = await client.PostAsync("/api/v1/assets", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/v1/assets/", response.Headers.Location?.OriginalString[..15]);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;
        Assert.NotEqual(Guid.Empty, root.GetProperty("id").GetGuid());
        Assert.Equal("Authorization Letter.pdf", root.GetProperty("originalFileName").GetString());
        Assert.Equal("application/pdf", root.GetProperty("contentType").GetString());
        Assert.Equal(SamplePdf.Length, root.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(64, root.GetProperty("sha256").GetString()!.Length);
        Assert.Equal("AuthorizationLetter", root.GetProperty("purpose").GetString());
        Assert.Equal("Private", root.GetProperty("visibility").GetString());
    }

    [Fact]
    public async Task InvalidFileType_ReturnsValidationError()
    {
        using var client = ClientFor(Uploader);
        var bytes = Encoding.ASCII.GetBytes("malicious script");
        using var form = FileContent(bytes, "evil.sh", "application/x-sh");

        var response = await client.PostAsync("/api/v1/assets", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EmptyFile_ReturnsValidationError()
    {
        using var client = ClientFor(Uploader);
        using var form = FileContent(Array.Empty<byte>(), "empty.pdf", "application/pdf");

        var response = await client.PostAsync("/api/v1/assets", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TooLargeFile_ReturnsValidationError()
    {
        using var client = ClientFor(Uploader);
        // 10 MB + 1 byte. The factory keeps the production limit so this
        // exercises the real check path.
        var oversized = new byte[10 * 1024 * 1024 + 1];
        // Make the first bytes look like a PDF so content-type passes; only
        // size should trip.
        Array.Copy(SamplePdf, oversized, SamplePdf.Length);
        using var form = FileContent(oversized, "huge.pdf", "application/pdf");

        var response = await client.PostAsync("/api/v1/assets", form);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    // -- metadata -------------------------------------------------------------

    [Fact]
    public async Task Metadata_ReturnsSavedInfo()
    {
        var id = await UploadAsAsync(Uploader);

        using var client = ClientFor(Uploader);
        var response = await client.GetAsync($"/api/v1/assets/{id}/metadata");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;
        Assert.Equal(id, root.GetProperty("id").GetGuid());
        Assert.Equal("doc.pdf", root.GetProperty("originalFileName").GetString());
        Assert.Equal("application/pdf", root.GetProperty("contentType").GetString());
        Assert.Equal(SamplePdf.Length, root.GetProperty("sizeBytes").GetInt64());
    }

    [Fact]
    public async Task Metadata_PrivateAsset_AnonymousReturnsUnauthorized()
    {
        var id = await UploadAsAsync(Uploader, visibility: "Private");

        using var client = AnonClient();
        var response = await client.GetAsync($"/api/v1/assets/{id}/metadata");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Metadata_PrivateAsset_NonOwnerNonAdminReturnsForbidden()
    {
        var id = await UploadAsAsync(Uploader, visibility: "Private");

        using var client = ClientFor(OtherUser);
        var response = await client.GetAsync($"/api/v1/assets/{id}/metadata");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Metadata_PrivateAsset_AdminAllowed()
    {
        var id = await UploadAsAsync(Uploader, visibility: "Private");

        using var client = ClientFor(Admin);
        var response = await client.GetAsync($"/api/v1/assets/{id}/metadata");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Metadata_UnknownId_ReturnsNotFound()
    {
        using var client = ClientFor(Uploader);
        var response = await client.GetAsync($"/api/v1/assets/{Guid.NewGuid()}/metadata");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -- download -------------------------------------------------------------

    [Fact]
    public async Task Download_ReturnsUploadedContent()
    {
        var id = await UploadAsAsync(Uploader);

        using var client = ClientFor(Uploader);
        var response = await client.GetAsync($"/api/v1/assets/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(SamplePdf, bytes);
    }

    [Fact]
    public async Task PublicAsset_CanBeDownloadedAnonymously()
    {
        var id = await UploadAsAsync(Uploader, visibility: "Public");

        using var client = AnonClient();
        var response = await client.GetAsync($"/api/v1/assets/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(SamplePdf, bytes);
    }

    [Fact]
    public async Task PrivateAsset_Anonymous_ReturnsUnauthorized()
    {
        var id = await UploadAsAsync(Uploader, visibility: "Private");

        using var client = AnonClient();
        var response = await client.GetAsync($"/api/v1/assets/{id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -- delete ---------------------------------------------------------------

    [Fact]
    public async Task Delete_SoftDeletesAsset()
    {
        var id = await UploadAsAsync(Uploader);

        using var client = ClientFor(Uploader);
        var del = await client.DeleteAsync($"/api/v1/assets/{id}");

        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }

    [Fact]
    public async Task Delete_NonOwnerNonAdmin_ReturnsForbidden()
    {
        var id = await UploadAsAsync(Uploader);

        using var client = ClientFor(OtherUser);
        var response = await client.DeleteAsync($"/api/v1/assets/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Anonymous_ReturnsUnauthorized()
    {
        var id = await UploadAsAsync(Uploader);

        using var client = AnonClient();
        var response = await client.DeleteAsync($"/api/v1/assets/{id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Download_DeletedAsset_ReturnsNotFound()
    {
        var id = await UploadAsAsync(Uploader);

        using (var client = ClientFor(Uploader))
        {
            var del = await client.DeleteAsync($"/api/v1/assets/{id}");
            Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        }

        using (var client = ClientFor(Uploader))
        {
            // Even the uploader cannot retrieve a deleted row — the global
            // soft-delete query filter hides it from the SELECT.
            var get = await client.GetAsync($"/api/v1/assets/{id}");
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

            var meta = await client.GetAsync($"/api/v1/assets/{id}/metadata");
            Assert.Equal(HttpStatusCode.NotFound, meta.StatusCode);
        }
    }
}
