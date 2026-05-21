namespace Matloob.Api.Infrastructure.Storage;

/// <summary>
/// Output of <see cref="IFileStorage.SaveAsync"/>. Captures everything the
/// upload handler needs to persist an <c>Asset</c> row — except the original
/// file name and content type, which are caller-supplied.
/// </summary>
/// <param name="StoredFileName">
/// Generated on-disk leaf filename, including extension. Driver-internal;
/// never echoed via API.
/// </param>
/// <param name="RelativePath">
/// Driver-scoped path of the saved blob (e.g. <c>2026/05/21/abc...bin</c>).
/// What the storage driver later expects in <see cref="IFileStorage.OpenReadAsync"/>.
/// Never echoed via API.
/// </param>
/// <param name="SizeBytes">Exact byte count written.</param>
/// <param name="Sha256Hex">Lower-case hex SHA-256 of the bytes.</param>
public sealed record StoredFileResult(
    string StoredFileName,
    string RelativePath,
    long SizeBytes,
    string Sha256Hex);
