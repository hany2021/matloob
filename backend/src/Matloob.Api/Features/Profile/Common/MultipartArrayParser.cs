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
}
