using McpHub.Modules.GitHub.Models.Api;

namespace McpHub.Modules.GitHub.Models;

/// <summary>Pure translation from GitHub API shapes to the compact records returned to the model.</summary>
internal static class GitHubMapper
{
    private const int MaxDescriptionChars = 2_000;
    private const int MaxCommitMessageChars = 500;

    public static RepositorySummary ToRepositorySummary(ApiRepo r) =>
        new(r.FullName, r.Description, r.Private, r.Archived, r.Language, r.StargazersCount, r.DefaultBranch, r.PushedAt, r.HtmlUrl);

    public static IssueSummary ToIssueSummary(ApiIssue i) =>
        new(i.Number, i.Title, i.State, i.PullRequest is not null, i.User?.Login,
            [.. (i.Labels ?? []).Select(l => l.Name)], i.CreatedAt, i.UpdatedAt, i.HtmlUrl);

    public static PullRequestDetails ToPullRequestDetails(ApiPullRequest pr, IEnumerable<ApiPullFile> files) =>
        new(pr.Number, pr.Title, pr.State, pr.Draft, pr.Merged, pr.User?.Login, pr.Head.Ref, pr.Base.Ref,
            Truncate(pr.Body, MaxDescriptionChars), pr.Additions, pr.Deletions, pr.ChangedFiles, pr.Commits,
            pr.CreatedAt, pr.UpdatedAt, pr.HtmlUrl,
            [.. files.Select(f => new FileChange(f.Filename, f.Status, f.Additions, f.Deletions))]);

    public static CommitSummary ToCommitSummary(ApiCommit c) =>
        new(c.Sha, Truncate(c.Commit.Message, MaxCommitMessageChars) ?? string.Empty, c.Commit.Author?.Name, c.Commit.Author?.Date, c.HtmlUrl);

    public static WorkflowRunSummary ToWorkflowRunSummary(ApiWorkflowRun r) =>
        new(r.Id, r.Name, r.Status, r.Conclusion, r.HeadBranch, r.Event, r.CreatedAt, r.HtmlUrl);

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max] + "…";
}
