using System.Text.Json.Serialization;

namespace Matloob.Api.Features.Common;

/// <summary>
/// Laravel-style single-resource envelope: <c>{ "data": { ... } }</c>.
///
/// The old Laravel API wrapped every API Resource response in a top-level
/// <c>data</c> key, and the public frontend's read hooks are typed
/// <c>ApiResponse&lt;T&gt; = { data: T }</c> accordingly (they consume
/// <c>response.data.data</c>). Endpoints migrated from a Laravel Resource must
/// therefore wrap their payload in this envelope to stay wire-compatible.
///
/// Ad-hoc JSON endpoints (e.g. notifications <c>unread-count</c>, which
/// returned a bare <c>{ count }</c> in Laravel) intentionally do NOT use this.
/// </summary>
public sealed record DataEnvelope<T>(
    [property: JsonPropertyName("data")] T Data) : IBypassEnvelope;
