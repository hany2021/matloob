using System.Text;
using System.Text.Json.Serialization;
using FluentValidation.Results;

namespace Matloob.Api.Features.Common;

/// <summary>
/// Laravel-style validation error body: <c>{ "message": ..., "errors": {
/// "field_name": ["msg", ...] } }</c> returned with HTTP 422. The public
/// frontend uses <c>laravel-precognition</c>, which keys field errors by the
/// snake_case field name it submitted and expects a 422 status — so this maps
/// FastEndpoints/FluentValidation's PascalCase property names to snake_case and
/// is wired as the global <c>Errors.ResponseBuilder</c> in <c>Program.cs</c>
/// (with <c>Errors.StatusCode = 422</c>).
///
/// Already-formatted keys (Laravel bracket arrays like <c>education[0][degree]</c>
/// emitted by the multipart profile mutators, or keys already containing
/// <c>_</c>/<c>.</c>) are left untouched.
/// </summary>
public sealed class ValidationErrorResponse : IBypassEnvelope
{
    [JsonPropertyName("message")]
    public string Message { get; init; } = "The given data was invalid.";

    [JsonPropertyName("errors")]
    public IReadOnlyDictionary<string, string[]> Errors { get; init; }
        = new Dictionary<string, string[]>();

    public static ValidationErrorResponse FromFailures(IReadOnlyList<ValidationFailure> failures)
    {
        var errors = failures
            .GroupBy(f => ToFieldName(f.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray());

        return new ValidationErrorResponse { Errors = errors };
    }

    /// <summary>
    /// Convert a FluentValidation property name to the snake_case field name the
    /// frontend submitted. Leaves Laravel bracket keys and keys that already
    /// contain <c>_</c> or <c>.</c> untouched.
    /// </summary>
    private static string ToFieldName(string name)
    {
        if (string.IsNullOrEmpty(name)
            || name.Contains('[') || name.Contains('_') || name.Contains('.'))
        {
            return name;
        }

        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
