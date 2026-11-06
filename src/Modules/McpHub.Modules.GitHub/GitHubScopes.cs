namespace McpHub.Modules.GitHub;

/// <summary>Scopes a caller's token must carry. Writes and deletes have their own scopes.</summary>
public static class GitHubScopes
{
    public const string Read = "github.read";
    public const string Write = "github.write";
    public const string Delete = "github.delete";
}
