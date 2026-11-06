namespace McpHub.Host.Security;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Disabling is only honoured in the Development environment; startup fails otherwise.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Issuer URL of your identity provider (OpenID Connect discovery is used to fetch signing keys).</summary>
    public string? Authority { get; set; }

    /// <summary>The audience this server accepts, i.e. this MCP server's identifier in the identity provider.</summary>
    public string? Audience { get; set; }

    /// <summary>Public https base URL of this server, e.g. https://mcp.[YOUR_DOMAIN]. Used for OAuth discovery metadata.</summary>
    public string? PublicBaseUrl { get; set; }

    public string[] ScopesSupported { get; set; } = [];
}
