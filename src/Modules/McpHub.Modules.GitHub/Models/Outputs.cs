namespace McpHub.Modules.GitHub.Models;

// What tools return to the model: compact, flat, and free of fields it does not need (fewer tokens, less data exposure).
// Author names/logins are personal data; they are included because they are normal repository metadata.

public sealed record RepositorySummary(
    string FullName,
    string? Description,
    bool IsPrivate,
    bool IsArchived,
    string? Language,
    int Stars,
    string DefaultBranch,
    DateTimeOffset? PushedAt,
    string Url);

public sealed record FileContent(
    string Path,
    string? Ref,
    long SizeBytes,
    bool Truncated,
    string Content);

public sealed record IssueSummary(
    int Number,
    string Title,
    string State,
    bool IsPullRequest,
    string? Author,
    IReadOnlyList<string> Labels,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Url);

public sealed record IssueSearchResult(int TotalCount, IReadOnlyList<IssueSummary> Items);

public sealed record FileChange(string Path, string Status, int Additions, int Deletions);

public sealed record PullRequestDetails(
    int Number,
    string Title,
    string State,
    bool IsDraft,
    bool IsMerged,
    string? Author,
    string HeadBranch,
    string BaseBranch,
    string? Description,
    int Additions,
    int Deletions,
    int ChangedFiles,
    int Commits,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Url,
    IReadOnlyList<FileChange> Files);

public sealed record CommitSummary(
    string Sha,
    string Message,
    string? Author,
    DateTimeOffset? Date,
    string Url);

public sealed record WorkflowRunSummary(
    long Id,
    string? Name,
    string Status,
    string? Conclusion,
    string? Branch,
    string Event,
    DateTimeOffset CreatedAt,
    string Url);
