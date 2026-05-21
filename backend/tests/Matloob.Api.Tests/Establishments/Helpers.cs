using System.Net.Http.Json;
using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Tests.Auth;
using Matloob.Domain.Assets;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Establishments;

/// <summary>
/// Shared fixtures for the onboarding lifecycle tests. Helpers here insert
/// Asset rows directly into the test InMemory DB so each test can avoid
/// going through the LocalFileStorage upload path (which would require
/// disk + a different factory).
/// </summary>
internal static class Helpers
{
    public static readonly TestUser Creator = new(
        Sub: "estab-creator-1",
        Roles: new[] { "matloob_user" });

    public static readonly TestUser OtherUser = new(
        Sub: "estab-other-user",
        Roles: new[] { "matloob_user" });

    public static readonly TestUser Admin = new(
        Sub: "estab-admin-1",
        Roles: new[] { "matloob_admin" },
        Audiences: new[] { "matloob:admin" });

    /// <summary>
    /// Build a complete-but-still-Draft establishment with both documents
    /// already linked. Returns the establishment id; the asset ids are an
    /// internal detail.
    /// </summary>
    /// <param name="creatorSub">Sub claim of <paramref name="creatorClient"/>'s
    /// authenticated principal. Used as the seeded Assets' OwnerUserId so
    /// the link endpoints' ownership check passes. Defaults to
    /// <see cref="Creator"/>.Sub.</param>
    public static async Task<Guid> CreateReadyToSubmitDraftAsync(
        EstablishmentsApiFactory factory,
        HttpClient creatorClient,
        string commercialRegistrationNumber,
        string? creatorSub = null)
    {
        creatorSub ??= Creator.Sub;

        // 1. Draft.
        var draftResponse = await creatorClient.PostAsync(
            "/api/v1/establishments/registration/drafts", content: null);
        draftResponse.EnsureSuccessStatusCode();
        var id = await ReadIdAsync(draftResponse);

        // 2. Fill in the §3.1 fields.
        var basicInfo = new
        {
            name = "Acme Events Co",
            commercialRegistrationNumber,
            laborOfficeId = "12345",
            sequenceNumber = "67890",
            city = "Riyadh",
            email = "ops@acme.test",
            phone = "+966500000000",
        };
        var patch = await creatorClient.PatchAsJsonAsync(
            $"/api/v1/establishments/registration/{id}/basic-info", basicInfo);
        patch.EnsureSuccessStatusCode();

        // 3. Seed two Assets directly into the DB so we can link them.
        Guid authLetterAssetId, crAssetId;
        using (var scope = factory.CreateDbScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            authLetterAssetId = Guid.NewGuid();
            crAssetId = Guid.NewGuid();
            db.Assets.AddRange(
                MakeAsset(authLetterAssetId, AssetPurpose.AuthorizationLetter, creatorSub),
                MakeAsset(crAssetId, AssetPurpose.CommercialRegistration, creatorSub));
            await db.SaveChangesAsync();
        }

        // 4. Link both documents.
        var letterLink = await creatorClient.PostAsJsonAsync(
            $"/api/v1/establishments/registration/{id}/documents/authorization-letter",
            new { assetId = authLetterAssetId });
        letterLink.EnsureSuccessStatusCode();

        var crLink = await creatorClient.PostAsJsonAsync(
            $"/api/v1/establishments/registration/{id}/documents/commercial-registration",
            new { assetId = crAssetId });
        crLink.EnsureSuccessStatusCode();

        return id;
    }

    public static Asset MakeAsset(Guid id, AssetPurpose purpose, string ownerSub) =>
        new Asset(
            id: id,
            originalFileName: $"{purpose}.pdf",
            storedFileName: $"{id:N}.pdf",
            contentType: "application/pdf",
            sizeBytes: 1024,
            sha256: new string('a', 64),
            relativePath: $"test/{id:N}.pdf",
            storageDriver: AssetStorageDriver.Local,
            visibility: AssetVisibility.Private,
            purpose: purpose,
            ownerUserId: ownerSub);

    public static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream);
        return doc.RootElement.GetProperty("id").GetGuid();
    }
}
