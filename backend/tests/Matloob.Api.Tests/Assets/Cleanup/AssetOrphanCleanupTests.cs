using Matloob.Api.Infrastructure.Persistence;
using Matloob.Api.Infrastructure.Storage;
using Matloob.Api.Infrastructure.Storage.Cleanup;
using Matloob.Domain.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matloob.Api.Tests.Assets.Cleanup;

/// <summary>
/// Orphan-cleanup integration tests. Each test seeds an Asset row + its
/// physical file under the factory's temp <c>StorageRoot</c>, then advances
/// the test clock past the retention window (or not, depending on the
/// scenario) and calls <see cref="AssetOrphanCleanupService.CleanupAsync"/>
/// directly. Asserts against the filesystem AND the DB.
/// </summary>
public sealed class AssetOrphanCleanupTests : IAsyncLifetime
{
    // Per-test factory: each test gets its own InMemory DB + temp storage
    // root + test clock. The cleanup pass scans the whole table, so any
    // state leak between tests inflates the Examined / Deleted counters
    // and breaks the per-test assertions. Fresh factory per test is the
    // cleanest way to keep the counts honest.
    private CleanupApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new CleanupApiFactory();
        // Force the host to build now (resolves the InMemory provider, the
        // test clock, and the cleanup service registration) so test code
        // can use the factory immediately without an HTTP call.
        _ = _factory.Services;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    // -- helpers -------------------------------------------------------------

    /// <summary>
    /// Insert one Asset row + put a file on disk at its relative path.
    /// Returns the (id, absolute-path) pair so tests can assert on both.
    /// </summary>
    private async Task<(Guid Id, string AbsolutePath)> SeedAssetAsync(
        bool softDeleted,
        DateTimeOffset? deletedAt = null)
    {
        var assetId = Guid.NewGuid();
        var storedFileName = $"{assetId:N}.pdf";
        var partition = "2026/01/01";
        var relativePath = $"{partition}/{storedFileName}";
        var absolutePath = Path.Combine(_factory.StorageRoot, "2026", "01", "01", storedFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await File.WriteAllBytesAsync(absolutePath, new byte[] { 1, 2, 3, 4 });

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var asset = new Asset(
            id: assetId,
            originalFileName: "doc.pdf",
            storedFileName: storedFileName,
            contentType: "application/pdf",
            sizeBytes: 4,
            sha256: new string('a', 64),
            relativePath: relativePath,
            storageDriver: AssetStorageDriver.Local,
            visibility: AssetVisibility.Private,
            purpose: AssetPurpose.Generic,
            ownerUserId: "test-user");
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        if (softDeleted)
        {
            // Re-load WITH the soft-delete filter ignored so we can mutate
            // the lifecycle columns. SoftDeleteInterceptor stamps DeletedAt
            // = TestTimeProvider.Now if we call Remove(), but tests need to
            // backdate that, so set the columns directly here.
            var tracked = await db.Assets
                .IgnoreQueryFilters()
                .SingleAsync(a => a.Id == assetId);
            tracked.GetType().GetProperty(nameof(Asset.IsDeleted))!
                .SetValue(tracked, true);
            tracked.GetType().GetProperty(nameof(Asset.DeletedAt))!
                .SetValue(tracked, deletedAt ?? _factory.Clock.GetUtcNow());
            await db.SaveChangesAsync();
        }

        return (assetId, absolutePath);
    }

    private async Task<CleanupSummary> RunCleanupAsync()
    {
        using var scope = _factory.CreateDbScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<AssetOrphanCleanupService>();
        return await cleanup.CleanupAsync(CancellationToken.None);
    }

    // -- tests ---------------------------------------------------------------

    [Fact]
    public async Task ActiveAsset_FileIsNotDeleted()
    {
        var (id, path) = await SeedAssetAsync(softDeleted: false);

        var summary = await RunCleanupAsync();

        Assert.True(File.Exists(path), "active asset file must survive cleanup");
        Assert.Equal(0, summary.Examined);

        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Assets.AnyAsync(a => a.Id == id));
    }

    [Fact]
    public async Task SoftDeletedAsset_WithinRetention_FileIsNotDeleted()
    {
        // Soft-delete at "now"; retention is 30 days, so the cleanup pass
        // should pass it by.
        var (_, path) = await SeedAssetAsync(softDeleted: true,
            deletedAt: _factory.Clock.GetUtcNow());

        var summary = await RunCleanupAsync();

        Assert.True(File.Exists(path),
            "asset just inside the retention window must survive cleanup");
        Assert.Equal(0, summary.Examined);
    }

    [Fact]
    public async Task SoftDeletedAsset_PastRetention_FileIsDeleted_RowSurvives()
    {
        // Soft-delete 31 days ago; one day past the 30-day retention.
        var (id, path) = await SeedAssetAsync(softDeleted: true,
            deletedAt: _factory.Clock.GetUtcNow().AddDays(-31));

        var summary = await RunCleanupAsync();

        Assert.False(File.Exists(path),
            "file past retention must be removed by cleanup");
        Assert.Equal(1, summary.Examined);
        Assert.Equal(1, summary.Deleted);

        // The DB row STAYS as audit -- cleanup never touches DB rows.
        using var scope = _factory.CreateDbScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stillThere = await db.Assets
            .IgnoreQueryFilters()
            .AnyAsync(a => a.Id == id && a.IsDeleted);
        Assert.True(stillThere, "DB row must survive the file delete");
    }

    [Fact]
    public async Task MissingFile_DoesNotFailCleanup_CountsAsAlreadyGone()
    {
        var (_, path) = await SeedAssetAsync(softDeleted: true,
            deletedAt: _factory.Clock.GetUtcNow().AddDays(-31));

        // Delete the file BEFORE the cleanup pass, simulating "a previous
        // pass partially succeeded" or "an operator removed it by hand".
        File.Delete(path);

        var summary = await RunCleanupAsync();

        Assert.Equal(1, summary.Examined);
        Assert.Equal(0, summary.Deleted);
        Assert.Equal(1, summary.AlreadyGone);
        Assert.Equal(0, summary.Errored);
    }

    [Fact]
    public async Task Cleanup_IsIdempotent()
    {
        var (_, path) = await SeedAssetAsync(softDeleted: true,
            deletedAt: _factory.Clock.GetUtcNow().AddDays(-31));

        var first = await RunCleanupAsync();
        Assert.Equal(1, first.Deleted);
        Assert.False(File.Exists(path));

        // Second pass over the same row: file is already gone, count it but
        // do not fail.
        var second = await RunCleanupAsync();
        Assert.Equal(1, second.Examined);
        Assert.Equal(0, second.Deleted);
        Assert.Equal(1, second.AlreadyGone);
    }

    [Fact]
    public async Task Cleanup_ProcessesMultipleEligibleRowsInOnePass()
    {
        var asset1 = await SeedAssetAsync(softDeleted: true,
            deletedAt: _factory.Clock.GetUtcNow().AddDays(-31));
        var asset2 = await SeedAssetAsync(softDeleted: true,
            deletedAt: _factory.Clock.GetUtcNow().AddDays(-60));
        var fresh = await SeedAssetAsync(softDeleted: true,
            deletedAt: _factory.Clock.GetUtcNow().AddDays(-1)); // inside retention.

        var summary = await RunCleanupAsync();

        Assert.Equal(2, summary.Examined);
        Assert.Equal(2, summary.Deleted);
        Assert.False(File.Exists(asset1.AbsolutePath));
        Assert.False(File.Exists(asset2.AbsolutePath));
        Assert.True(File.Exists(fresh.AbsolutePath),
            "asset still inside retention window must not be touched");
    }

    [Fact]
    public async Task ClockAdvance_MakesAFreshDeleteEligible()
    {
        var (_, path) = await SeedAssetAsync(softDeleted: true,
            deletedAt: _factory.Clock.GetUtcNow());

        // Pass #1 immediately after the soft-delete: nothing to do.
        var beforeAdvance = await RunCleanupAsync();
        Assert.Equal(0, beforeAdvance.Examined);
        Assert.True(File.Exists(path));

        // Advance "now" by 31 days. The same row should now be picked up.
        _factory.Clock.Advance(TimeSpan.FromDays(31));

        var afterAdvance = await RunCleanupAsync();
        Assert.Equal(1, afterAdvance.Examined);
        Assert.Equal(1, afterAdvance.Deleted);
        Assert.False(File.Exists(path));
    }
}
