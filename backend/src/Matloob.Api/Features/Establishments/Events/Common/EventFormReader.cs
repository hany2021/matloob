using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Matloob.Api.Features.Establishments.Events.Common;

/// <summary>
/// Reads the multi-step event wizard payload as an <see cref="IFormCollection"/>
/// regardless of how the public frontend sent it:
///
/// <list type="bullet">
///   <item><b>multipart/form-data</b> — steps that carry file uploads
///     (step-two uploads, nested opportunity uploads). Read verbatim via
///     <see cref="HttpRequest.ReadFormAsync(CancellationToken)"/>.</item>
///   <item><b>application/json</b> — every file-less step plus the
///     laravel-precognition validation pings. The wizard's
///     <c>useForm('post', …)</c> serializes plain objects as JSON (axios only
///     switches to multipart when the data actually contains <c>File</c>s), so
///     step one and the pings arrive as JSON. The endpoints used to
///     <c>AllowFileUploads()</c>, which restricts them to multipart and 415s
///     JSON — this reader is what lets one route serve both shapes (same
///     pattern as <c>CreateOpportunityEndpoint</c>).</item>
/// </list>
///
/// <para>
/// The JSON branch flattens the nested body into the exact Laravel
/// bracket-style keys <see cref="EventWriteSupport"/> /
/// <see cref="SuccessCriteriaSupport"/> / <see cref="EventOpportunitiesSupport"/>
/// already parse, so validation + apply stay single-path:
/// </para>
/// <code>
/// { "step_one": { "name": "x" } }                        → step_one[name]=x
/// { "step_four": { "opportunities_categories": ["a"] } } → step_four[opportunities_categories][0]=a
/// { "opportunities": [ { "name": "y" } ] }               → opportunities[0][name]=y
/// { "step_two": null }                                   → (no keys → HasStep stays false)
/// </code>
///
/// <para>
/// Throws <see cref="JsonException"/> on malformed JSON; the endpoint maps that
/// to a 400. JSON bodies never carry files, so the resulting form has an empty
/// <see cref="IFormCollection.Files"/> collection.
/// </para>
/// </summary>
internal static class EventFormReader
{
    public static async Task<IFormCollection> ReadAsync(HttpContext ctx, CancellationToken ct)
    {
        if (ctx.Request.HasFormContentType)
        {
            return await ctx.Request.ReadFormAsync(ct);
        }

        // Empty body (defensive — the frontend always sends one): treat as no
        // steps present so create yields the normal "step one required" 422.
        if (ctx.Request.ContentLength == 0)
        {
            return new FormCollection(new Dictionary<string, StringValues>());
        }

        using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ct);
        var fields = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        Flatten(string.Empty, doc.RootElement, fields);
        return new FormCollection(fields);
    }

    private static void Flatten(string prefix, JsonElement element, Dictionary<string, StringValues> acc)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = prefix.Length == 0 ? prop.Name : $"{prefix}[{prop.Name}]";
                    Flatten(key, prop.Value, acc);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Flatten($"{prefix}[{index++}]", item, acc);
                }
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                // Skip — keeps HasStep(form, "step_x") false for null steps,
                // matching the multipart shape where absent steps send no keys.
                break;

            default:
                // Scalar. A scalar at the root has no key to bind to — ignore.
                if (prefix.Length != 0)
                {
                    acc[prefix] = ScalarValue(element);
                }
                break;
        }
    }

    private static string ScalarValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        // Numbers: GetRawText() preserves the literal (e.g. "24.7", "50") in
        // invariant form, which the decimal/int/date parsers expect.
        _ => element.GetRawText(),
    };
}
