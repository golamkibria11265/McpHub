using System.ComponentModel;
using McpHub.Core.Tools;
using McpHub.Modules.GitHub.Models;
using McpHub.Modules.GitHub.Services;
using ModelContextProtocol.Server;

namespace McpHub.Modules.GitHub.Tools;

/// <summary>MCP-facing adapter only: describes the tools to the model and delegates. No logic here.</summary>
[McpServerToolType]
public sealed class RepositoryTools(RepositoryService service, IToolExecutor executor)
{
    [McpServerTool(Name = "github_list_repositories", ReadOnly = true, Idempotent = true)]
    [Description("Lists repositories of an allowed GitHub organization or user, most recently pushed first.")]
    public Task<IReadOnlyList<RepositorySummary>> ListRepositories(
        [Description("GitHub organization or user name.")] string owner,
        [Description("Maximum number of repositories to return (server enforces an upper limit).")] int? count = null,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("github_list_repositories", GitHubScopes.Read,
            () => service.ListAsync(owner, count, cancellationToken));

    [McpServerTool(Name = "github_get_file", ReadOnly = true, Idempotent = true)]
    [Description("Reads one text file from a repository. The path must be a file, not a directory. Large files are truncated. Files that commonly hold secrets are blocked.")]
    public Task<FileContent> GetFile(
        [Description("GitHub organization or user name.")] string owner,
        [Description("Repository name.")] string repo,
        [Description("Repository-relative file path using '/', e.g. 'src/Program.cs'.")] string path,
        [Description("Optional branch, tag or commit SHA. Defaults to the default branch.")] string? gitRef = null,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("github_get_file", GitHubScopes.Read,
            () => service.GetFileAsync(owner, repo, path, gitRef, cancellationToken));
}
