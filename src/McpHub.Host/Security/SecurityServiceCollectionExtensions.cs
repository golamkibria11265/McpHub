using System.Text.RegularExpressions;
using McpHub.Core.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Net.Http.Headers;

namespace McpHub.Host.Security;

internal static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddMcpHubSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection(AuthOptions.SectionName);
        var auth = section.Get<AuthOptions>() ?? new AuthOptions();
        services.Configure<AuthOptions>(section);

        services.AddHttpContextAccessor();
        services.AddScoped<ICallerContext, HttpCallerContext>();
        services.AddAuthorization();

        if (!auth.Enabled)
        {
            // Fail closed: a misconfigured production server must never start without authentication.
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException("Auth:Enabled=false is only allowed in the Development environment.");
            }

            services
                .AddAuthentication(DevelopmentAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthHandler>(DevelopmentAuthHandler.SchemeName, null);

            return services;
        }

        if (string.IsNullOrWhiteSpace(auth.Authority) || string.IsNullOrWhiteSpace(auth.Audience))
        {
            throw new InvalidOperationException("Auth:Authority and Auth:Audience are required when authentication is enabled.");
        }

        // Fail at startup with a readable message instead of a generic 500 on every request (typical on a first
        // IIS or Linux deploy where appsettings.Production.json still holds the template placeholders).
        if (!environment.IsDevelopment()
            && (HasPlaceholder(auth.Authority) || HasPlaceholder(auth.Audience) || HasPlaceholder(auth.PublicBaseUrl)))
        {
            throw new InvalidOperationException(
                "Auth:Authority, Auth:Audience and Auth:PublicBaseUrl still contain template placeholders such as [YOUR_DOMAIN]. " +
                "Set real values in appsettings.Production.json on the server.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = auth.Authority;
                options.Audience = auth.Audience;
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.MapInboundClaims = false; // keep claim names exactly as issued ("sub", "scope")
                options.TokenValidationParameters.NameClaimType = "sub";
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidateLifetime = true;

                // MCP authorization: tell the client where to discover the authorization server (RFC 9728).
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

                        var metadataUrl = OAuthMetadataEndpoint.MetadataUrl(auth);
                        context.Response.Headers[HeaderNames.WWWAuthenticate] = metadataUrl is null
                            ? "Bearer"
                            : $"Bearer resource_metadata=\"{metadataUrl}\"";

                        return Task.CompletedTask;
                    }
                };
            });

        return services;
    }

    private static bool HasPlaceholder(string? value) =>
        !string.IsNullOrEmpty(value) && Regex.IsMatch(value, @"\[[A-Z][A-Z0-9_]*\]");
}
