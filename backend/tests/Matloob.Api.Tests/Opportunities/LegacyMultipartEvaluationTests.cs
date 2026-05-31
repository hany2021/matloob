using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Common;
using Matloob.Domain.Offers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Opportunities;

/// <summary>
/// Verifies POST /api/establishments/evaluations accepts both JSON and
/// the legacy Laravel <c>multipart/form-data</c> shape with inline
/// <c>uploads[]</c> file parts.
/// </summary>
public sealed class LegacyMultipartEvaluationTests
    : IClassFixture<OpportunitiesApiFactory>, IAsyncLifetime
{
    private readonly OpportunitiesApiFactory _factory;
    private Guid _senderEstablishmentId;
    private Guid _offerId;

    public LegacyMultipartEvaluationTests(OpportunitiesApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.Worker.Sub);
        await OaoHelpers.SeedLocalUserAsync(_factory, OaoHelpers.EstablishmentOwner.Sub);

        _senderEstablishmentId = await OaoHelpers.SeedApprovedEstablishmentAsync(
            _factory, OaoHelpers.EstablishmentOwner.Sub, "CR-LEGACY-MP-EVAL");

        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _senderEstablishmentId, name: "MP eval opp", forVacancy: true);
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, oppId, applicantUserId: OaoHelpers.Worker.Sub);
        _offerId = await OaoHelpers.SeedOfferAsync(
            _factory, _senderEstablishmentId, oppId, appId,
            sentByUserId: OaoHelpers.EstablishmentOwner.Sub,
            status: OfferStatus.Accepted,
            acceptedAt: DateTimeOffset.UtcNow);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task LegacyMultipart_OneUpload_Returns201_AndPersistsEvaluationAsset()
    {
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(_offerId.ToString()), "offer_id");
        content.Add(new StringContent("5"), "rating");
        content.Add(new StringContent("true"), "recommend_for_future_opportunities");
        content.Add(new StringContent("Great work overall"), "comment");
        content.Add(new StringContent("4"), "matloob_evaluation");

        var fileBytes = Encoding.UTF8.GetBytes("%PDF-1.4 fake pdf content");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "uploads", "evidence.pdf");

        var response = await client.PostAsync(
            $"/api/v1/establishments/{_senderEstablishmentId}/evaluations", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var data = doc.RootElement.DataOf();
        var evaluationId = data.GetProperty("id").GetGuid();
        // No 'contract' field anywhere in the response.
        Assert.False(data.TryGetProperty("contract", out _));

        // Verify EvaluationAsset row + Asset row exist.
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assetLinks = await db.EvaluationAssets
            .Where(x => x.EvaluationId == evaluationId)
            .ToListAsync();
        Assert.Single(assetLinks);
        var asset = await db.Assets.FirstAsync(a => a.Id == assetLinks[0].AssetId);
        Assert.Equal("evidence.pdf", asset.OriginalFileName);
    }

    [Fact]
    public async Task LegacyMultipart_LaravelArrayKeyName_Accepted()
    {
        // Laravel's StoreEvaluationRequest validation rule is "uploads.*";
        // the actual form key is `uploads` (PHP collapses bracket syntax).
        // We accept both `uploads` and `uploads[]` for safety.
        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);

        // Use a different offer to avoid the dup-evaluation guard.
        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _senderEstablishmentId, name: "MP eval opp 2", forVacancy: true);
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, oppId, applicantUserId: OaoHelpers.Worker.Sub);
        var offerId = await OaoHelpers.SeedOfferAsync(
            _factory, _senderEstablishmentId, oppId, appId,
            sentByUserId: OaoHelpers.EstablishmentOwner.Sub,
            status: OfferStatus.Accepted,
            acceptedAt: DateTimeOffset.UtcNow);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(offerId.ToString()), "offer_id");
        content.Add(new StringContent("4"), "rating");
        content.Add(new StringContent("false"), "recommend_for_future_opportunities");

        var fileBytes = new byte[] { 0xFF, 0xD8, 0xFF }; // mock JPEG header
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "uploads[]", "photo.jpg");

        var response = await client.PostAsync(
            $"/api/v1/establishments/{_senderEstablishmentId}/evaluations", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task LegacyMultipart_NoUploads_StillCreatesEvaluation()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _senderEstablishmentId, name: "MP eval no-files", forVacancy: true);
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, oppId, applicantUserId: OaoHelpers.Worker.Sub);
        var offerId = await OaoHelpers.SeedOfferAsync(
            _factory, _senderEstablishmentId, oppId, appId,
            sentByUserId: OaoHelpers.EstablishmentOwner.Sub,
            status: OfferStatus.Accepted,
            acceptedAt: DateTimeOffset.UtcNow);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(offerId.ToString()), "offer_id");
        content.Add(new StringContent("3"), "rating");
        content.Add(new StringContent("true"), "recommend_for_future_opportunities");

        var response = await client.PostAsync(
            $"/api/v1/establishments/{_senderEstablishmentId}/evaluations", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var evaluationId = doc.RootElement.DataOf().GetProperty("id").GetGuid();

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.EvaluationAssets.CountAsync(x => x.EvaluationId == evaluationId);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task JsonRequestPath_StillWorks_AfterMultipartSupportAdded()
    {
        var oppId = await OaoHelpers.SeedOpportunityAsync(
            _factory, _senderEstablishmentId, name: "JSON eval", forVacancy: true);
        var appId = await OaoHelpers.SeedApplicationAsync(
            _factory, oppId, applicantUserId: OaoHelpers.Worker.Sub);
        var offerId = await OaoHelpers.SeedOfferAsync(
            _factory, _senderEstablishmentId, oppId, appId,
            sentByUserId: OaoHelpers.EstablishmentOwner.Sub,
            status: OfferStatus.Accepted,
            acceptedAt: DateTimeOffset.UtcNow);

        var client = _factory.CreateClientFor(OaoHelpers.EstablishmentOwner);
        var response = await client.PostAsync(
            $"/api/v1/establishments/{_senderEstablishmentId}/evaluations",
            new StringContent(
                $$"""
                { "offer_id": "{{offerId}}", "rating": 5, "recommend_for_future_opportunities": true }
                """,
                Encoding.UTF8,
                "application/json"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
