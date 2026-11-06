using System.ComponentModel;
using McpHub.Core.Tools;
using McpHub.Modules.GitHub.Access;
using McpHub.Modules.GitHub.Models;
using McpHub.Modules.GitHub.Services;
using ModelContextProtocol.Server;

namespace McpHub.Modules.GitHub.Tools;

[McpServerToolType]
public sealed class WorkflowTools(WorkflowService service, RepositoryAccessPolicy access, IToolExecutor executor)
{
    [McpServerTool(Name = "github_list_workflow_runs", ReadOnly = true, Idempotent = true)]
    [Description("Lists recent GitHub Actions workflow runs of a repository with their status and conclusion.")]
    public Task<IReadOnlyList<WorkflowRunSummary>> ListWorkflowRuns(
        [Description("Repository name.")] string repo,
        [Description("Optional branch name to filter by.")] string? branch = null,
        [Description("Maximum number of runs (server enforces an upper limit).")] int? count = null,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("github_list_workflow_runs", GitHubScopes.Read,
            () => service.ListRunsAsync(access.ConfiguredOwner, repo, branch, count, cancellationToken));
}
