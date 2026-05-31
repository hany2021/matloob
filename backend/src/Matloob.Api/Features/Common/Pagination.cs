using System.Text.Json.Serialization;

namespace Matloob.Api.Features.Common;

/// <summary>
/// Laravel-style list envelope: <c>{ "data": [...], "meta": {...}, "links": {...} }</c>.
/// The public frontend's <c>ApiResponseWithPagination&lt;T&gt;</c> parser reads
/// <c>data</c> plus <c>meta.current_page</c> / <c>meta.last_page</c> for
/// infinite scroll. Produced by <see cref="ResponseEnvelopeShim"/> for bare
/// list responses, and used directly by the notifications feed stub.
/// </summary>
public sealed class PaginationEnvelope : IBypassEnvelope
{
    /// <summary>Declared <c>object</c> so System.Text.Json serializes the
    /// collection's runtime type (the generic-root pitfall does not apply to
    /// <c>object</c>-typed properties).</summary>
    [JsonPropertyName("data")]
    public object Data { get; init; } = Array.Empty<object>();

    [JsonPropertyName("meta")]
    public PaginationMeta Meta { get; init; } = new();

    [JsonPropertyName("links")]
    public PaginationLinks Links { get; init; } = new();
}

/// <summary>Laravel pagination <c>meta</c> block — snake_case field names the
/// frontend depends on byte-for-byte.</summary>
public sealed class PaginationMeta
{
    [JsonPropertyName("current_page")]
    public int CurrentPage { get; init; }

    [JsonPropertyName("from")]
    public int From { get; init; }

    [JsonPropertyName("last_page")]
    public int LastPage { get; init; }

    [JsonPropertyName("links")]
    public IReadOnlyList<object> Links { get; init; } = [];

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("per_page")]
    public int PerPage { get; init; }

    [JsonPropertyName("to")]
    public int To { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }
}

/// <summary>Laravel pagination <c>links</c> block.</summary>
public sealed class PaginationLinks
{
    [JsonPropertyName("first")]
    public string? First { get; init; }

    [JsonPropertyName("last")]
    public string? Last { get; init; }

    [JsonPropertyName("prev")]
    public string? Prev { get; init; }

    [JsonPropertyName("next")]
    public string? Next { get; init; }
}
