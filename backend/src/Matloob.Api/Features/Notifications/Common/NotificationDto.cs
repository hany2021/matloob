using System.Text.Json.Serialization;

namespace Matloob.Api.Features.Notifications.Common;

/// <summary>
/// Wire shape of one notification in the feed — mirrors the legacy Laravel
/// <c>NotificationResource</c>.
/// </summary>
public sealed record NotificationDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("resource")] object? Resource,
    [property: JsonPropertyName("resource_id")] string? ResourceId,
    [property: JsonPropertyName("resource_type")] string? ResourceType,
    [property: JsonPropertyName("image")] string? Image,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("is_read")] bool IsRead);
