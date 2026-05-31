using System.Text.Json;

namespace Matloob.Api.Tests.Common;

/// <summary>
/// Helpers for asserting against the global <c>{ data }</c> / <c>{ data, meta,
/// links }</c> response envelope applied to the legacy public-frontend routes
/// (<c>/api/users/*</c>, <c>/api/establishments/*</c>) by
/// <c>ResponseEnvelopeShim</c>. Canonical <c>/api/v1/*</c> routes stay bare.
/// </summary>
internal static class EnvelopeTestExtensions
{
    /// <summary>Unwraps the <c>data</c> element of an enveloped response.</summary>
    public static JsonElement DataOf(this JsonElement root) => root.GetProperty("data");

    /// <summary>
    /// Returns the raw JSON of the <c>data</c> payload from an enveloped
    /// response string — used to compare a wrapped legacy response against a
    /// bare canonical one.
    /// </summary>
    public static string UnwrapData(string envelopedJson)
    {
        using var doc = JsonDocument.Parse(envelopedJson);
        return doc.RootElement.GetProperty("data").GetRawText();
    }
}
