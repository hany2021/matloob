using Matloob.Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Matloob.Api.Infrastructure.Auth;

/// <summary>
/// Composition root for authentication. Wires JwtBearer against NEC
/// IdentityServer using values bound from the <c>Identity</c> configuration
/// section.
///
/// This commit only registers the services. No endpoint requires
/// authentication yet — the API starts successfully even when IdM is
/// unreachable, because OIDC discovery is lazy: JwtBearer fetches JWKS only
/// on the first request that needs a token validated.
/// </summary>
public static class AuthRegistration
{
    public static IServiceCollection AddMatloobAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // IHttpContextAccessor is the bridge JwtCurrentUser uses to find the
        // current request's authenticated principal. Outside a request scope
        // (EF design-time, Quartz jobs, container init) HttpContext is null
        // and JwtCurrentUser falls back to "system" — exactly what audit needs.
        services.AddHttpContextAccessor();
        services.AddSingleton<ICurrentUser, JwtCurrentUser>();

        // Local user sync: on every authenticated request, create-or-
        // update the row in the local `users` table keyed by IdM sub.
        // Service is scoped (per-request) so it shares the request's
        // AppDbContext + ICurrentUser instance.
        services.AddScoped<
            Matloob.Api.Infrastructure.Identity.UserSync.ICurrentUserSyncService,
            Matloob.Api.Infrastructure.Identity.UserSync.CurrentUserSyncService>();

        var section = configuration.GetSection(IdentityOptions.SectionName);

        // Bind for IOptions<IdentityOptions> consumers (e.g. handlers that need
        // to read Authority for federated logout in later commits).
        services.Configure<IdentityOptions>(section);

        // Read once at startup to configure JwtBearer. JwtBearerOptions itself
        // is a singleton-style options object so we do not need IOptionsMonitor
        // here; config reloads would need a process restart.
        var identity = section.Get<IdentityOptions>() ?? new IdentityOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = identity.Authority;
                jwt.Audience = identity.Audience;
                jwt.RequireHttpsMetadata = identity.RequireHttpsMetadata;

                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = !string.IsNullOrEmpty(identity.Issuer),
                    ValidIssuer = identity.Issuer,

                    ValidateAudience = true,
                    ValidAudiences = BuildAudienceList(identity),

                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    NameClaimType = identity.NameClaimType,
                    RoleClaimType = identity.RoleClaimType,

                    // Tolerate small clock differences between the API host and IdM.
                    ClockSkew = TimeSpan.FromMinutes(2),
                };
            });

        // Typed HttpClient for the IdM readiness check.
        // In dev IdM uses a self-signed cert (RequireHttpsMetadata=false), so
        // we relax server-cert validation to match JwtBearer's stance. In prod
        // IdM presents a real cert and we want full validation.
        services.AddHttpClient<IdentityServerHealthCheck>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = identity.RequireHttpsMetadata
                ? null
                : HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        });

        services.AddAuthorization(options =>
        {
            // Policy.User — /api/v1/users/*
            //   - authenticated bearer token
            //   - matloob_user role on the principal
            options.AddPolicy(MatloobPolicies.User, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole("matloob_user"));

            // Policy.Admin — /api/v1/admin/*
            //   - authenticated bearer token
            //   - matloob_admin role
            //   - AND `aud` claim matches the configured AdminAudience.
            // JwtBearer's ValidAudiences already accepts either matloob:api or
            // matloob:admin at the authentication layer; this extra RequireClaim
            // narrows admin endpoints to the matloob:admin audience specifically,
            // so a regular user token cannot reach admin routes even if it has
            // the matloob_admin role by mistake.
            options.AddPolicy(MatloobPolicies.Admin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole("matloob_admin");

                if (!string.IsNullOrEmpty(identity.AdminAudience))
                {
                    policy.RequireClaim("aud", identity.AdminAudience);
                }
            });

            // (The earlier "matloob.establishment-context" policy was removed
            // during the Phase-8 polish pass. Establishment endpoints now
            // resolve context from the URL id + MembershipChecks rather
            // than an X-Commissioner-UUID header.)
        });

        return services;
    }

    private static IEnumerable<string> BuildAudienceList(IdentityOptions identity)
    {
        if (!string.IsNullOrEmpty(identity.Audience))
        {
            yield return identity.Audience;
        }

        if (!string.IsNullOrEmpty(identity.AdminAudience))
        {
            yield return identity.AdminAudience;
        }
    }
}
