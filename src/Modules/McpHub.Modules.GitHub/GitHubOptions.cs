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

    /// <summary>Master switch for create/update tools (issues, comments, branches, file commits, pull requests). Off by default.</summary>
    public bool AllowWrite { get; set; }

    /// <summary>Extra switch for deleting files and branches. Only effective together with <see cref="AllowWrite"/>.</summary>
    public bool AllowDelete { get; set; }

    /// <summary>Extra switch for merging pull requests. Only effective together with <see cref="AllowWrite"/>.</summary>
    public bool AllowMerge { get; set; }

    /// <summary>
    /// When false (default) commits and branch deletions are refused on the default branch and on <see cref="ProtectedBranches"/>,
    /// so changes go through a branch and a pull request.
    /// </summary>
    public bool AllowCommitToProtectedBranches { get; set; }

    /// <summary>Branches treated as protected in addition to the repository's default branch.</summary>
    public string[] ProtectedBranches { get; set; } = ["main", "master", "release", "production"];

    /// <summary>Largest file content (characters) accepted in one commit.</summary>
    public int MaxWriteChars { get; set; } = 200_000;

    public int ClampCount(int? requested) => Math.Clamp(requested ?? MaxResults, 1, MaxResults);
}
