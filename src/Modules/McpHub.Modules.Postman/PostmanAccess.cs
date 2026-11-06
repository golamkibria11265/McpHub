using McpHub.Core.Errors;
using Microsoft.Extensions.Options;

namespace McpHub.Modules.Postman;

/// <summary>Decides which workspaces may be touched and whether writes or deletes are switched on. All from configuration.</summary>
public sealed class PostmanAccess(IOptions<PostmanOptions> options)
{
    private PostmanOptions Options => options.Value;

    public IReadOnlyList<string> Workspaces => Options.AllowedWorkspaceIds;

    /// <summary>Returns the workspace to use. Only a value from configuration is ever accepted.</summary>
    public string ResolveWorkspace(string? requested)
    {
        var allowed = Options.AllowedWorkspaceIds;
        if (string.IsNullOrWhiteSpace(requested))
        {
            return allowed.Length == 1
                ? allowed[0]
                : throw Fail(IntegrationErrorKind.InvalidInput, "Several workspaces are configured; specify which one to use (see postman_list_workspaces).");
        }

        return allowed.FirstOrDefault(w => string.Equals(w, requested.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? throw Fail(IntegrationErrorKind.Forbidden, "That workspace is not allowed on this server.");
    }

    public void RequireWrite()
    {
        if (!Options.AllowWrite)
        {
            throw Fail(IntegrationErrorKind.Forbidden, "Write operations are disabled on this server.");
        }
    }

    public void RequireDelete()
    {
        RequireWrite();
        if (!Options.AllowDelete)
        {
            throw Fail(IntegrationErrorKind.Forbidden, "Delete operations are disabled on this server.");
        }
    }

    /// <summary>Ids placed in URL paths must be plain identifiers (no path tricks, no query injection).</summary>
    public static string SafeId(string? id, string what)
    {
        var value = id?.Trim() ?? string.Empty;
        return value.Length is > 0 and <= 80 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_')
            ? value
            : throw Fail(IntegrationErrorKind.InvalidInput, $"'{what}' is not a valid Postman id.");
    }

    internal static IntegrationException Fail(IntegrationErrorKind kind, string message) =>
        new(kind, PostmanModule.IntegrationName, message);
}
