namespace McpHub.Modules.GitHub;

/// <summary>Scopes a caller's token must carry. Write tools (later) get their own scope, e.g. "github.write".</summary>
public static class GitHubScopes
{
    public const string Read = "github.read";
}
