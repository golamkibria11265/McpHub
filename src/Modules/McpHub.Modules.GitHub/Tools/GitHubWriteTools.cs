using System.ComponentModel;
using McpHub.Core.Tools;
using McpHub.Modules.GitHub.Models;
using McpHub.Modules.GitHub.Services;
using ModelContextProtocol.Server;

namespace McpHub.Modules.GitHub.Tools;

/// <summary>MCP-facing adapter for the write tools. Rules and switches live in GitHubWriteService; the owner comes from configuration.</summary>
[McpServerToolType]
public sealed class GitHubWriteTools(GitHubWriteService service, IToolExecutor executor)
{
    [McpServerTool(Name = "github_create_issue", Destructive = false, Idempotent = false)]
    [Description("Creates an issue in a repository of the configured owner. Never include secrets. Requires writes to be enabled.")]
    public Task<WriteOutcome> CreateIssue(
        [Description("Repository name.")] string repo,
        [Description("Issue title.")] string title,
        [Description("Issue body (Markdown).")] string? body = null,
        [Description("Optional labels.")] string[]? labels = null,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("github_create_issue", GitHubScopes.Write,
            () => service.CreateIssueAsync(repo, title, body, labels, cancellationToken));

    [McpServerTool(Name = "github_update_issue", Destructive = false, Idempotent = true)]
    [Description("Changes the title, body, state (open/closed) or labels of an issue or pull request. Only the values you pass are changed. Requires writes to be enabled.")]
    public Task<WriteOutcome> UpdateIssue(
        [Description("Repository name.")] string repo,
        [Description("Issue or pull request number.")] int number,
        [Description("New title.")] string? title = null,
        [Description("New body (replaces the old one).")] string? body = null,
        [Description("'open' or 'closed'.")] string? state = null,
        [Description("Replacement list of labels.")] string[]? labels = null,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("github_update_issue", GitHubScopes.Write,
            () => service.UpdateIssueAsync(repo, number, title, body, state, labels, cancellationToken));

    [McpServerTool(Name = "github_add_comment", Destructive = false, Idempotent = false)]
    [Description("Adds a comment to an issue or pull request. Never include secrets. Requires writes to be enabled.")]
    public Task<WriteOutcome> AddComment(
        [Description("Repository name.")] string repo,
        [Description("Issue or pull request number.")] int number,
        [Description("Comment text (Markdown).")] string body,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("github_add_comment", GitHubScopes.Write,
            () => service.AddCommentAsync(repo, number, body, cancellationToken));

    [McpServerTool(Name = "github_create_branch", Destructive = false, Idempotent = false)]
    [Description("Creates a branch from another branch (default: the repository's default branch). Requires writes to be enabled.")]
    public Task<WriteOutcome> CreateBranch(
        [Description("Repository name.")] string repo,
        [Description("New branch name.")] string branch,
        [Description("Branch to start from. Defaults to the default branch.")] string? fromRef = null,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("github_create_branch", GitHubScopes.Write,
            () => service.CreateBranchAsync(repo, branch, fromRef, cancellationToken));

    [McpServerTool(Name = "github_put_file", Destructive = true, Idempotent = true)]
    [Description("Creates or replaces one text file with a commit on a branch. Commits to the default or protected branches are refused (use a branch and a pull request). Files that commonly hold secrets, and content that contains secrets, are refused. Requires writes to be enabled.")]
    public Task<WriteOutcome> PutFile(
        [Description("Repository name.")] string repo,
        [Description("Repository-relative path using '/'.")] string path,
        [Description("Complete new file content (text).")] string content,
        [Description("Commit message.")] string message,
        [Description("Branch to commit to (not the default branch).")] string branch,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("github_put_file", GitHubScopes.Write,
            () => service.PutFileAsync(repo, path, content, message, branch, cancellationToken));

    [McpServerTool(Name = "github_create_pull_request", Destructive = false, Idempotent = false)]
    [Description("Opens a pull request from a branch into another branch. Requires writes to be enabled.")]
    public Task<WriteOutcome> CreatePullRequest(
        [Description("Repository name.")] string repo,
        [Description("Pull request title.")] string title,
        [Description("Branch with the changes.")] string head,
        [Description("Branch to merge into, e.g. the default branch.")] string baseBranch,
        [Description("Description (Markdown).")] string? body = null,
        [Description("Open as a draft.")] bool draft = false,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("github_create_pull_request", GitHubScopes.Write,
            () => service.CreatePullRequestAsync(repo, title, head, baseBranch, body, draft, cancellationToken));

    [McpServerTool(Name = "github_merge_pull_request", Destructive = true, Idempotent = false)]
    [Description("Merges a pull request. Needs writes and merging to be enabled. Always ask the user before merging.")]
    public Task<WriteOutcome> MergePullRequest(
        [Description("Repository name.")] string repo,
        [Description("Pull request number.")] int number,
        [Description("'merge' (default), 'squash' or 'rebase'.")] string? method = null,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("github_merge_pull_request", GitHubScopes.Write,
            () => service.MergePullRequestAsync(repo, number, method, cancellationToken));

    [McpServerTool(Name = "github_delete_file", Destructive = true, Idempotent = false)]
    [Description("Deletes one file with a commit on a branch (never the default or protected branches). Requires deletes to be enabled. Ask the user first.")]
    public Task<WriteOutcome> DeleteFile(
        [Description("Repository name.")] string repo,
        [Description("Repository-relative path using '/'.")] string path,
        [Description("Commit message.")] string message,
        [Description("Branch to commit to (not the default branch).")] string branch,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("github_delete_file", GitHubScopes.Delete,
            () => service.DeleteFileAsync(repo, path, message, branch, cancellationToken));

    [McpServerTool(Name = "github_delete_branch", Destructive = true, Idempotent = false)]
    [Description("Deletes a branch (never the default or a protected branch). Requires deletes to be enabled. Ask the user first.")]
    public Task<WriteOutcome> DeleteBranch(
        [Description("Repository name.")] string repo,
        [Description("Branch to delete.")] string branch,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("github_delete_branch", GitHubScopes.Delete,
            () => service.DeleteBranchAsync(repo, branch, cancellationToken));
}
