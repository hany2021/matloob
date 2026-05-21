using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Matloob.Api.Infrastructure.Auth;

/// <summary>
/// Readiness check that confirms NEC IdentityServer is reachable. Calls the
/// OIDC discovery document, then the <c>jwks_uri</c> it returns. Failure means
/// the API would not be able to validate any bearer token, so readiness must
/// flip to Unhealthy (-> 503 on /health/ready) and orchestrators should hold
/// off traffic until IdM recovers.
///
/// Tagged "ready" so it is filtered into /health/ready only — /health (liveness)
/// continues to report green even if IdM is offline.
/// </summary>
internal sealed class IdentityServerHealthCheck : IHealthCheck
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly HttpClient _httpClient;
    private readonly IdentityOptions _options;
    private readonly ILogger<IdentityServerHealthCheck> _logger;

    public IdentityServerHealthCheck(
        HttpClient httpClient,
        IOptions<IdentityOptions> options,
        ILogger<IdentityServerHealthCheck> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Empty Authority is a configuration error — surface it loudly via
        // Unhealthy (not Degraded). In dev that means the readiness endpoint
        // turns red until the Identity:Authority value is filled in; that is
        // exactly what we want — a missing IdM URL would silently break every
        // protected endpoint at first call.
        if (string.IsNullOrWhiteSpace(_options.Authority))
        {
            return HealthCheckResult.Unhealthy(
                "Identity:Authority is not configured. Set ConnectionStrings/Identity in appsettings.* or env vars.");
        }

        var discoveryUrl = $"{_options.Authority.TrimEnd('/')}/.well-known/openid-configuration";

        try
        {
            using var discoveryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            discoveryCts.CancelAfter(ProbeTimeout);

            using var discoveryResponse = await _httpClient.GetAsync(discoveryUrl, discoveryCts.Token);
            if (!discoveryResponse.IsSuccessStatusCode)
            {
                return HealthCheckResult.Unhealthy(
                    $"IdM discovery at {discoveryUrl} returned HTTP {(int)discoveryResponse.StatusCode}.");
            }

            var discoveryDoc = await discoveryResponse.Content
                .ReadFromJsonAsync<DiscoveryDocument>(discoveryCts.Token);
            var jwksUri = discoveryDoc?.JwksUri;

            if (string.IsNullOrEmpty(jwksUri))
            {
                return HealthCheckResult.Unhealthy(
                    $"IdM discovery at {discoveryUrl} did not return a jwks_uri.");
            }

            using var jwksCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            jwksCts.CancelAfter(ProbeTimeout);

            using var jwksResponse = await _httpClient.GetAsync(jwksUri, jwksCts.Token);
            if (!jwksResponse.IsSuccessStatusCode)
            {
                return HealthCheckResult.Unhealthy(
                    $"IdM JWKS at {jwksUri} returned HTTP {(int)jwksResponse.StatusCode}.");
            }

            return HealthCheckResult.Healthy(
                $"IdM discovery + JWKS reachable at {_options.Authority}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy(
                "Health check was cancelled before IdM responded.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IdM JWKS health check failed");
            return HealthCheckResult.Unhealthy(
                $"Could not reach IdM at {_options.Authority}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed record DiscoveryDocument(
        [property: JsonPropertyName("jwks_uri")] string? JwksUri);
}
