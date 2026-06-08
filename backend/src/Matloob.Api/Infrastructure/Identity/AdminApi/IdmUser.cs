using System.Text.Json.Serialization;

namespace Matloob.Api.Infrastructure.Identity.AdminApi;

/// <summary>
/// Subset of the IdM <c>GetUserDetailsById</c> / <c>CreateUser</c> response we
/// consume. IdM returns more fields; we map only what the admin feature needs.
/// </summary>
public sealed record IdmUser(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("roles")] IReadOnlyList<string>? Roles);
