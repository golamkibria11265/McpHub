namespace McpHub.Modules.GitHub;

public sealed class GitHubOptions
{
    public string BaseUrl { get; set; } = "https://api.github.com/";

    /// <summary>
    /// GitHub users/organizations the tools may touch. Required (startup fails when empty): the server never
    /// reaches beyond this list even if the token itself has wider access.
    /// </summary>
    public string[] AllowedOwners { get; set; } = [];

    /// <summary>Upper bound on list sizes. Keeps responses (and LLM token cost) small.</summary>
    public int MaxResults { get; set; } = 30;

    /// <summary>Longest file content returned to the model; longer files are truncated and flagged.</summary>
    public int MaxFileChars { get; set; } = 50_000;

    public int ClampCount(int? requested) => Math.Clamp(requested ?? MaxResults, 1, MaxResults);
}
