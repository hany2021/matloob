using System.Text.Json.Serialization;

namespace Matloob.Domain.Assets;

/// <summary>
/// Who can download an asset without an explicit grant.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssetVisibility>))]
public enum AssetVisibility
{
    /// <summary>
    /// Owner + admin only. Anonymous downloads return 401; non-owner authenticated users return 403.
    /// Default for everything uploaded through the API.
    /// </summary>
    Private = 0,

    /// <summary>
    /// Anyone with the GUID can download. Reserved for assets that are intentionally
    /// shareable (e.g. season covers, public branding assets).
    /// </summary>
    Public = 1,
}
