using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Matloob.Api.Features.Profile.Common;

/// <summary>
/// Parses Laravel-style indexed multipart arrays (<c>education[0][degree]</c>,
/// <c>education[0][copy]</c> for a file, etc.) out of an
/// <see cref="IFormCollection"/>. The legacy frontend submits profile arrays
/// this way via laravel-precognition's FormData serialization, which neither
/// FastEndpoints model binding nor System.Text.Json understands.
/// </summary>
public static partial class MultipartArrayParser
{
    [GeneratedRegex(@"^(?<arr>\w+)\[(?<idx>\d+)\]\[(?<field>\w+)\]$")]
    private static partial Regex KeyPattern();

    /// <summary>One parsed array element: its scalar fields + any files.</summary>
    public sealed class Item
    {
        public Dictionary<string, string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IFormFile> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? Field(string name) => Fields.TryGetValue(name, out var v) ? v : null;
        public IFormFile? File(string name) => Files.TryGetValue(name, out var v) ? v : null;
        public bool Bool(string name) => Field(name) is { } s
            && (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the elements of <paramref name="arrayKey"/> ordered by their
    /// numeric index. Empty when the array is absent.
    /// </summary>
    public static IReadOnlyList<Item> Parse(IFormCollection form, string arrayKey)
    {
        var byIndex = new SortedDictionary<int, Item>();

        Item ItemAt(int idx)
        {
            if (!byIndex.TryGetValue(idx, out var item))
            {
                item = new Item();
                byIndex[idx] = item;
            }
            return item;
        }

        foreach (var (key, value) in form)
        {
            var m = KeyPattern().Match(key);
            if (!m.Success || !m.Groups["arr"].Value.Equals(arrayKey, StringComparison.OrdinalIgnoreCase))
                continue;
            var idx = int.Parse(m.Groups["idx"].Value);
            ItemAt(idx).Fields[m.Groups["field"].Value] = value.ToString();
        }

        foreach (var file in form.Files)
        {
            var m = KeyPattern().Match(file.Name);
            if (!m.Success || !m.Groups["arr"].Value.Equals(arrayKey, StringComparison.OrdinalIgnoreCase))
                continue;
            var idx = int.Parse(m.Groups["idx"].Value);
            ItemAt(idx).Files[m.Groups["field"].Value] = file;
        }

        return byIndex.Values.ToList();
    }

    /// <summary>
    /// JSON counterpart of <see cref="Parse"/> for precognition pre-validation
    /// requests that arrive without files. Expects a body shaped like
    /// <c>{ "education": [ { "degree": "...", "gpa_system": 4, ... }, ... ] }</c>.
    /// Files are absent (precognition never carries the binary), so the
    /// returned items have <see cref="Item.Files"/> empty — the handler skips
    /// the file-format / size checks for that branch on its own.
    /// </summary>
    public static async Task<IReadOnlyList<Item>> ParseJsonAsync(
        HttpContext ctx, string arrayKey, CancellationToken ct)
    {
        // Allow seeking so subsequent middleware (or logging) can re-read.
        ctx.Request.EnableBuffering();
        ctx.Request.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ct);
        ctx.Request.Body.Position = 0;

        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty(arrayKey, out var arr)
            || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<Item>();
        }

        var items = new List<Item>(arr.GetArrayLength());
        foreach (var el in arr.EnumerateArray())
        {
            var item = new Item();
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in el.EnumerateObject())
                {
                    // Skip null + objects + arrays: only flat scalar fields
                    // map onto Fields. Files would have been multipart; we
                    // intentionally never set Files here.
                    var value = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => null,
                    };
                    if (value is not null)
                    {
                        item.Fields[prop.Name] = value;
                    }
                }
            }
            items.Add(item);
        }
        return items;
    }
}
