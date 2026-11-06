namespace McpHub.Modules.GitHub.Models.Api;

// Raw GitHub REST shapes (snake_case on the wire). Internal on purpose: the model only ever sees the compact
// output records in the parent namespace, never raw API payloads.

internal sealed record ApiUser(string Login);

internal sealed record ApiLabel(string Name);

internal sealed record ApiRepo(
    string FullName,
    string? Description,
    bool Private,
    bool Archived,
    string? Language,
    int StargazersCount,
    string DefaultBranch,
    DateTimeOffset? PushedAt,
    string HtmlUrl);

internal sealed record ApiContent(
    string Path,
    string Type,
    long Size,
    string? Encoding,
    string? Content);

internal sealed record ApiIssue(
    int Number,
    string Title,
    string State,
    ApiUser? User,
    IReadOnlyList<ApiLabel>? Labels,
    object? PullRequest,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string HtmlUrl);

internal sealed record ApiSearchResult(int TotalCount, IReadOnlyList<ApiIssue> Items);

internal sealed record ApiBranchRef(string Ref);

internal sealed record ApiPullRequest(
    int Number,
    string Title,
    string State,
    bool Draft,
    bool Merged,
    ApiUser? User,
    ApiBranchRef Head,
    ApiBranchRef Base,
    string? Body,
    int Additions,
    int Deletions,
    int ChangedFiles,
    int Commits,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string HtmlUrl);

internal sealed record ApiPullFile(string Filename, string Status, int Additions, int Deletions);

internal sealed record ApiGitAuthor(string? Name, DateTimeOffset? Date);

internal sealed record ApiCommitDetail(string Message, ApiGitAuthor? Author);

internal sealed record ApiCommit(string Sha, ApiCommitDetail Commit, string HtmlUrl);

internal sealed record ApiWorkflowRun(
    long Id,
    string? Name,
    string Status,
    string? Conclusion,
    string? HeadBranch,
    string Event,
    DateTimeOffset CreatedAt,
    string HtmlUrl);

internal sealed record ApiWorkflowRuns(int TotalCount, IReadOnlyList<ApiWorkflowRun> WorkflowRuns);
