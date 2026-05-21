using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Matloob.Api.Infrastructure.Storage;

/// <summary>
/// Filesystem-backed <see cref="IFileStorage"/>. Layout:
/// <code>
/// {AssetsRoot}/{yyyy}/{MM}/{dd}/{guid}{.ext}
/// </code>
/// The Guid is the leaf filename — there is no establishment or user folder
/// in the layout because (a) callers find files by GUID via the DB row, not
/// by browsing, and (b) the layout has to be cheap to move into S3 later as
/// a flat object store.
///
/// Path traversal: the input <c>relativePath</c> on read/exists is canonicalized
/// and then re-checked to live under the configured root. Any attempt to
/// climb out throws <see cref="UnauthorizedAccessException"/>.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly FileStorageOptions _options;
    private readonly string _root;
    private readonly TimeProvider _clock;

    public LocalFileStorage(
        IOptions<FileStorageOptions> options,
        IHostEnvironment env,
        TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
        _root = ResolveRoot(_options.AssetsRoot, env.ContentRootPath);
    }

    public async Task<StoredFileResult> SaveAsync(
        Stream content,
        string originalExtension,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var now = _clock.GetUtcNow();
        // Date partition keeps any one folder bounded, which Postgres won't
        // care about but the filesystem (especially on Windows) does.
        var partition = $"{now:yyyy}/{now:MM}/{now:dd}";
        var partitionPath = Path.Combine(_root,
            now.Year.ToString("0000"),
            now.Month.ToString("00"),
            now.Day.ToString("00"));
        Directory.CreateDirectory(partitionPath);

        var ext = SanitizeExtension(originalExtension);
        var storedName = $"{Guid.NewGuid():N}{ext}";
        var absolutePath = Path.Combine(partitionPath, storedName);

        // Compute SHA-256 streamingly so a 10 MB upload never holds two
        // copies in memory.
        using var sha = SHA256.Create();
        long size = 0;

        // FileShare.None: refuse to write if someone else has it open. The
        // filename is a fresh Guid so this should never actually contend.
        await using (var output = new FileStream(
            absolutePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                size += read;
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        }

        var hashHex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        // Always emit forward slashes in the persisted relative path so the
        // value is portable across OSes and renderable in URLs / logs.
        var relativePath = $"{partition}/{storedName}";

        return new StoredFileResult(storedName, relativePath, size, hashHex);
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken)
    {
        var absolute = ResolveInsideRoot(relativePath);
        if (!File.Exists(absolute))
        {
            throw new FileNotFoundException($"Asset blob not found.", relativePath);
        }

        // Hand the stream off to the caller; ASP.NET / FastEndpoints disposes
        // it once the response finishes streaming.
        Stream stream = new FileStream(
            absolute,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            var absolute = ResolveInsideRoot(relativePath);
            return Task.FromResult(File.Exists(absolute));
        }
        catch (UnauthorizedAccessException)
        {
            // The path tried to climb out of the root. To the world that just
            // looks like a missing asset; we don't leak the violation.
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Public for tests that need to confirm where the driver wrote.
    /// </summary>
    public string Root => _root;

    private string ResolveInsideRoot(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path is required.", nameof(relativePath));
        }

        // Reject anything that's already absolute or that tries to escape.
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Contains("..", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Refused suspicious relative path.");
        }

        // Normalize separators for Path.Combine on Windows.
        var safe = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(_root, safe));

        // Belt-and-suspenders: confirm the resolved path is still under the
        // root after symlink + relative-segment expansion.
        var rootNormalized = EnsureTrailingSeparator(Path.GetFullPath(_root));
        if (!combined.StartsWith(rootNormalized, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Refused suspicious relative path.");
        }

        return combined;
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static string ResolveRoot(string configuredRoot, string contentRoot)
    {
        var resolved = Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(contentRoot, configuredRoot);
        resolved = Path.GetFullPath(resolved);
        Directory.CreateDirectory(resolved);
        return resolved;
    }

    /// <summary>
    /// Whitelist a small set of characters in the extension so a malicious
    /// upload can't tunnel ".\\foo" through the leaf filename. Anything
    /// outside [a-zA-Z0-9] becomes an empty extension.
    /// </summary>
    private static string SanitizeExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return string.Empty;
        }

        var trimmed = extension.StartsWith('.') ? extension[1..] : extension;
        if (trimmed.Length is 0 or > 10)
        {
            return string.Empty;
        }

        foreach (var c in trimmed)
        {
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'))
            {
                return string.Empty;
            }
        }

        return "." + trimmed.ToLowerInvariant();
    }
}
