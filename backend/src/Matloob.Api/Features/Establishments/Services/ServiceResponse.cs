using System.Text.Json.Serialization;

namespace Matloob.Api.Features.Establishments.Services;

/// <summary>
/// Wire shape of an establishment service — mirrors the legacy Laravel
/// <c>ServiceResource</c> (<c>{ id, name, description }</c>). Wrapped in the
/// global <c>{ data }</c> envelope by the response shim.
/// </summary>
public sealed record ServiceResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description);
