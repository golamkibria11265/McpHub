using McpHub.Core.Security;
using Microsoft.Extensions.Options;

namespace McpHub.Host.Security;

/// <summary>Reads the caller's identity and scopes from the validated bearer token of the current request.</summary>
internal sealed class HttpCallerContext(IHttpContextAccessor accessor, IOptions<AuthOptions> auth) : ICallerContext
{
    public string UserId =>
        accessor.HttpContext?.User.FindFirst("sub")?.Value
        ?? accessor.HttpContext?.User.Identity?.Name
        ?? "anonymous";

    public bool HasScope(string scope)
    {
        // Auth can only be disabled in Development (enforced at startup), where every scope is granted.
        if (!auth.Value.Enabled)
        {
            return true;
        }

        var user = accessor.HttpContext?.User;
        if (user is null)
        {
            return false;
        }

        // "scope" is space-delimited (RFC 8693 / most IdPs); "scp" is used by Azure AD.
        return user.FindAll("scope").Concat(user.FindAll("scp"))
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
    }
}
