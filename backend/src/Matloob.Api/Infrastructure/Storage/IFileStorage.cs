namespace Matloob.Api.Infrastructure.Storage;

/// <summary>
/// Where uploaded file bytes live. v1 has one implementation
/// (<see cref="LocalFileStorage"/>); a future S3 / MinIO driver implements
/// the same contract.
///
/// The interface intentionally hides the physical layout — callers see only
/// the opaque <c>relativePath</c> returned by <see cref="SaveAsync"/>, plus
/// the <see cref="AssetStorageDriver"/> enum recorded on the row. This lets
/// us move bytes between drivers later without touching the upload /
/// download handlers.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Stream <paramref name="content"/> to the backing store, computing the
    /// SHA-256 on the fly. Caller passes the original file extension (with
    /// leading dot, or empty) so the stored name keeps a useful suffix for
    /// out-of-band inspection — the extension is sanitized internally.
    /// </summary>
    Task<StoredFileResult> SaveAsync(
        Stream content,
        string originalExtension,
        CancellationToken cancellationToken);

    /// <summary>
    /// Open a read-only stream over the previously-saved blob. The returned
    /// stream is owned by the caller (dispose it). Throws
    /// <see cref="FileNotFoundException"/> if the path is unknown or the file
    /// disappeared from disk.
    /// </summary>
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>True if the storage layer can serve the given relative path.</summary>
    Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>
    /// Physically remove the blob at <paramref name="relativePath"/>. Used
    /// by the orphan-cleanup background service to reclaim disk after the
    /// retention window passes — never by online API code.
    ///
    /// Idempotent: a missing file returns
    /// <see cref="DeleteResult.NotFound"/> and is NOT an error. Path-
    /// traversal attempts return <see cref="DeleteResult.Refused"/> so the
    /// caller can log + skip without crashing the loop. Successful removal
    /// returns <see cref="DeleteResult.Deleted"/>.
    /// </summary>
    Task<DeleteResult> DeleteAsync(string relativePath, CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of <see cref="IFileStorage.DeleteAsync"/>. Lets the cleanup
/// service log a single counter per category rather than chasing per-
/// exception types.
/// </summary>
public enum DeleteResult
{
    /// <summary>The file was on disk and is now gone.</summary>
    Deleted,

    /// <summary>The file was already missing — treated as success.</summary>
    NotFound,

    /// <summary>Path traversal or other safety violation. Caller should log + skip.</summary>
    Refused,
}
