using Microsoft.Extensions.Options;

namespace McpHub.Host.Security;

/// <summary>
/// Publishes OAuth 2.0 Protected Resource Metadata (RFC 9728) so MCP clients such as Claude can discover
/// which authorization server to use. Contains public information only.
/// </summary>
internal static class OAuthMetadataEndpoint
{
    private const string Path = "/.well-known/oauth-protected-resource";

    public static string? MetadataUrl(AuthOptions auth) =>
        string.IsNullOrWhiteSpace(auth.PublicBaseUrl) ? null : $"{auth.PublicBaseUrl.TrimEnd('/')}{Path}";

    public static IEndpointRouteBuilder MapOAuthProtectedResourceMetadata(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value;

        if (!auth.Enabled || string.IsNullOrWhiteSpace(auth.PublicBaseUrl) || string.IsNullOrWhiteSpace(auth.Authority))
        {
            return endpoints;
        }

        var document = new
        {
            resource = $"{auth.PublicBaseUrl.TrimEnd('/')}/mcp",
            authorization_servers = new[] { auth.Authority },
            scopes_supported = auth.ScopesSupported,
            bearer_methods_supported = new[] { "header" }
        };

        endpoints.MapGet(Path, () => Results.Json(document)).AllowAnonymous();
        return endpoints;
    }
}
