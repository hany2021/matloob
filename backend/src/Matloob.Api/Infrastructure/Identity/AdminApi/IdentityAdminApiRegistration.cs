using Microsoft.Extensions.Options;

namespace Matloob.Api.Infrastructure.Identity.AdminApi;

/// <summary>
/// DI wiring for the IdM admin API client. Binds <see cref="IdentityAdminApiOptions"/>
/// and registers <see cref="IIdentityAdminApi"/> as a typed HttpClient whose
/// BaseAddress + <c>x-api-key</c> header + TLS stance come from config.
///
/// When the base URL is blank (the default in source), the client is still
/// registered but every call throws a clear "not configured" error — the API
/// boots fine and only the admin-user endpoints are affected.
/// </summary>
public static class IdentityAdminApiRegistration
{
    public static IServiceCollection AddIdentityAdminApi(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(IdentityAdminApiOptions.SectionName);
        services.Configure<IdentityAdminApiOptions>(section);
        var options = section.Get<IdentityAdminApiOptions>() ?? new IdentityAdminApiOptions();

        services.AddHttpClient<IIdentityAdminApi, IdentityServerAdminApi>(client =>
        {
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            }
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds <= 0 ? 10 : options.TimeoutSeconds);
            client.DefaultRequestHeaders.Add("x-api-key", options.ApiKey ?? string.Empty);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = options.VerifySsl
                ? null
                : HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        });

        return services;
    }
}
