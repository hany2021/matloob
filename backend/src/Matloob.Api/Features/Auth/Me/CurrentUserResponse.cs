namespace Matloob.Api.Features.Auth.Me;

/// <summary>
/// Snapshot of the authenticated principal — for diagnostic / debugging use.
/// Never includes the raw token (it is opaque to the API anyway; JwtBearer
/// validates and discards).
/// </summary>
public sealed record CurrentUserResponse(
    bool IsAuthenticated,
    string? UserId,
    string? Name,
    string? AuthenticationType,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Audiences,
    IReadOnlyList<ClaimPair> Claims);

/// <summary>One claim in the principal's identity. Type/value pair, no metadata.</summary>
public sealed record ClaimPair(string Type, string Value);
