using System.ComponentModel;
using McpHub.Core.Tools;
using McpHub.Modules.GitHub.Access;
using McpHub.Modules.GitHub.Models;
using McpHub.Modules.GitHub.Services;
using ModelContextProtocol.Server;

namespace McpHub.Modules.GitHub.Tools;

[McpServerToolType]
public sealed class CommitTools(CommitService service, RepositoryAccessPolicy access, IToolExecutor executor)
{
    [McpServerTool(Name = "github_list_commits", ReadOnly = true, Idempotent = true)]
    [Description("Lists the most recent commits of a repository, optionally for one branch.")]
    public Task<IReadOnlyList<CommitSummary>> ListCommits(
        [Description("Repository name.")] string repo,
        [Description("Optional branch, tag or commit SHA. Defaults to the default branch.")] string? branch = null,
        [Description("Maximum number of commits (server enforces an upper limit).")] int? count = null,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("github_list_commits", GitHubScopes.Read,
            () => service.ListAsync(access.ConfiguredOwner, repo, branch, count, cancellationToken));
}
