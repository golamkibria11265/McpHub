namespace McpHub.Modules.GitHub;

public sealed class GitHubOptions
{
    public string BaseUrl { get; set; } = "https://api.github.com/";

    /// <summary>
    /// The single GitHub user or organization the tools work on. Required (startup fails unless exactly one name is
    /// given). The caller never supplies an owner: the server never reaches beyond this one even if the token could.
    /// </summary>
    public string[] AllowedOwners { get; set; } = [];

    /// <summary>Upper bound on list sizes. Keeps responses (and LLM token cost) small.</summary>
    public int MaxResults { get; set; } = 30;

    /// <summary>Longest file content returned to the model; longer files are truncated and flagged.</summary>
    public int MaxFileChars { get; set; } = 50_000;

    public int ClampCount(int? requested) => Math.Clamp(requested ?? MaxResults, 1, MaxResults);
}
