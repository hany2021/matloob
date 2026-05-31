using System.Text.Json.Serialization;

namespace Matloob.Api.Features.Establishments.Products;

/// <summary>
/// Wire shape of an establishment product — mirrors the legacy Laravel
/// <c>ProductResource</c> (<c>{ id, name, description }</c>). Wrapped in the
/// global <c>{ data }</c> envelope by the response shim.
/// </summary>
public sealed record ProductResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description);
