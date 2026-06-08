namespace Matloob.Api.Infrastructure.Identity.AdminApi;

/// <summary>
/// Thrown on any non-2xx response from the IdM admin API (or when it is not
/// configured). Mirrors the legacy <c>IdentityAdminApiException</c>. Endpoints
/// translate this into a 503/502 ProblemDetails so a transient IdM failure
/// never looks like a validation error.
/// </summary>
public sealed class IdentityAdminApiException : Exception
{
    public int? StatusCode { get; }

    public IdentityAdminApiException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public static IdentityAdminApiException FromHttpFailure(string operation, int status, string? body) =>
        new($"IdM admin API '{operation}' returned HTTP {status}.{(string.IsNullOrWhiteSpace(body) ? string.Empty : " " + body)}", status);
}
