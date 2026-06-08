using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Matloob.Api.Infrastructure.Identity.AdminApi;

/// <summary>
/// Typed-<see cref="HttpClient"/> implementation of <see cref="IIdentityAdminApi"/>.
/// BaseAddress + the <c>x-api-key</c> header + TLS stance are set at
/// registration (see <c>IdentityAdminApiRegistration</c>). All routes mirror
/// the legacy Laravel service 1:1.
/// </summary>
internal sealed class IdentityServerAdminApi : IIdentityAdminApi
{
    private static readonly JsonSerializerOptions JsonWebOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IdentityAdminApiOptions _options;

    public IdentityServerAdminApi(HttpClient http, IOptions<IdentityAdminApiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<IdmUser?> FindUserAsync(string emailOrId, CancellationToken ct)
    {
        EnsureConfigured();
        using var resp = await SendAsync("GetUserDetailsById",
            c => _http.GetAsync($"/api/Identity/GetUserDetailsById?userIdentifier={Uri.EscapeDataString(emailOrId)}", c), ct);

        // "Not found" varies by IdM deployment: a 404, OR a 204 No Content,
        // OR a 200 with a null/empty body. Treat all three as "no user".
        if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
        {
            return null;
        }
        if (!resp.IsSuccessStatusCode)
        {
            throw IdentityAdminApiException.FromHttpFailure("GetUserDetailsById", (int)resp.StatusCode, await SafeBody(resp, ct));
        }

        // Read the body as a string first so an empty 2xx body doesn't make
        // ReadFromJsonAsync throw ("no JSON tokens"); empty => no user.
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        IdmUser? body;
        try
        {
            body = JsonSerializer.Deserialize<IdmUser>(raw, JsonWebOptions);
        }
        catch (JsonException ex)
        {
            throw new IdentityAdminApiException(
                $"IdM admin API 'GetUserDetailsById' returned an unparseable body.", null, ex);
        }

        return body is not null && !string.IsNullOrEmpty(body.Id) ? body : null;
    }

    public async Task<IdmUser> CreateUserAsync(CreateIdmUserPayload payload, CancellationToken ct)
    {
        EnsureConfigured();
        var requestBody = new
        {
            email = payload.Email,
            userName = payload.UserName,
            name = payload.Name,
            password = payload.Password,            // null for AD users -> IdM creates with no password
            phoneNumber = (string?)null,
            roles = payload.Roles,
            claims = Array.Empty<object>(),
            // Forward-compat with the AD-native CreateUser support in the STS
            // (ignored by the currently-deployed IdM, which honours UserName +
            // no-password instead).
            isActiveDirectory = payload.IsActiveDirectory,
            samAccountName = payload.SamAccountName,
        };

        using var resp = await SendAsync("CreateUser",
            c => _http.PostAsJsonAsync("/api/Identity/CreateUser", requestBody, c), ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw IdentityAdminApiException.FromHttpFailure("CreateUser", (int)resp.StatusCode, await SafeBody(resp, ct));
        }

        var created = await resp.Content.ReadFromJsonAsync<IdmUser>(ct);
        if (created is null || string.IsNullOrEmpty(created.Id))
        {
            throw new IdentityAdminApiException("CreateUser succeeded but returned no user id.");
        }
        return created;
    }

    public async Task UpdateUserRolesAsync(
        string emailOrId, IReadOnlyList<string> rolesToAdd, IReadOnlyList<string> rolesToRemove, CancellationToken ct)
    {
        EnsureConfigured();
        var body = new { RolesToAdd = rolesToAdd, RolesToRemove = rolesToRemove };
        using var resp = await SendAsync("UpdateUserRoles",
            c => _http.PutAsJsonAsync($"/api/Identity/UpdateUserRoles?userIdentifier={Uri.EscapeDataString(emailOrId)}", body, c), ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw IdentityAdminApiException.FromHttpFailure("UpdateUserRoles", (int)resp.StatusCode, await SafeBody(resp, ct));
        }
    }

    public async Task DeactivateUserAsync(string emailOrId, CancellationToken ct)
    {
        EnsureConfigured();
        using var resp = await SendAsync("DeactivateUser",
            c => _http.PutAsync($"/api/Identity/DeactivateUser?userIdentifier={Uri.EscapeDataString(emailOrId)}", content: null, c), ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw IdentityAdminApiException.FromHttpFailure("DeactivateUser", (int)resp.StatusCode, await SafeBody(resp, ct));
        }
    }

    public async Task DeleteUserAsync(string emailOrId, CancellationToken ct)
    {
        EnsureConfigured();
        using var resp = await SendAsync("DeleteUser",
            c => _http.DeleteAsync($"/api/Identity/DeleteUser?userIdentifier={Uri.EscapeDataString(emailOrId)}", c), ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw IdentityAdminApiException.FromHttpFailure("DeleteUser", (int)resp.StatusCode, await SafeBody(resp, ct));
        }
    }

    /// <summary>
    /// Executes an IdM request and normalizes transport-level failures
    /// (unreachable host off-VPN, DNS, timeout) into
    /// <see cref="IdentityAdminApiException"/> so callers get a clean 502/503
    /// instead of an unhandled 500. Non-2xx HTTP responses are NOT handled here
    /// — each method inspects the status and builds its own message.
    /// </summary>
    private static async Task<HttpResponseMessage> SendAsync(
        string operation, Func<CancellationToken, Task<HttpResponseMessage>> call, CancellationToken ct)
    {
        try
        {
            return await call(ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new IdentityAdminApiException($"IdM admin API '{operation}' timed out.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new IdentityAdminApiException(
                $"IdM admin API '{operation}' is unreachable: {ex.Message}", null, ex);
        }
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new IdentityAdminApiException(
                "The IdM admin API is not configured (Identity:AdminApi:BaseUrl / ApiKey are blank).");
        }
    }

    private static async Task<string?> SafeBody(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct); }
        catch { return null; }
    }
}
