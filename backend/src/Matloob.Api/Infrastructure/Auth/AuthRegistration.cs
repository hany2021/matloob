using Microsoft.AspNetCore.Authentication.JwtBearer;
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

        services.AddAuthorization();

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
