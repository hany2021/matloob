using System.Text.Json;
using System.Text.Json.Serialization;

namespace Matloob.Api.Features.Common;

/// <summary>
/// Tolerant <see cref="Guid"/>? converter that treats empty / whitespace
/// strings as <c>null</c> instead of throwing a 400. The public frontend's
/// laravel-precognition forms emit <c>""</c> for new entries that don't have
/// a server-side id yet (e.g. a free-text skill the user just typed in), and
/// the legacy Laravel backend silently treated that as null. The new API
/// mirrors that behaviour so the frontend's request body can stay unchanged.
///
/// Apply via attribute on a nullable Guid property:
/// <code>
/// [JsonConverter(typeof(NullableGuidJsonConverter))]
/// public Guid? Id { get; init; }
/// </code>
/// </summary>
public sealed class NullableGuidJsonConverter : JsonConverter<Guid?>
{
    public override Guid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s))
                {
                    return null;
                }
                if (Guid.TryParse(s, out var g))
                {
                    return g;
                }
                throw new JsonException($"The JSON value '{s}' is not in a supported Guid format.");
            default:
                throw new JsonException($"Unexpected token {reader.TokenType} when parsing Guid.");
        }
    }

    public override void Write(Utf8JsonWriter writer, Guid? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value.Value);
        }
    }
}
