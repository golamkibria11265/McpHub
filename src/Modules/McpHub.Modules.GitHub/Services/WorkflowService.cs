using McpHub.Modules.GitHub.Access;
using McpHub.Modules.GitHub.Clients;
using McpHub.Modules.GitHub.Models;
using McpHub.Modules.GitHub.Models.Api;
using Microsoft.Extensions.Options;

namespace McpHub.Modules.GitHub.Services;

/// <summary>Use case: recent GitHub Actions workflow runs of a repository.</summary>
public sealed class WorkflowService(
    IGitHubApiClient api,
    RepositoryAccessPolicy access,
    IOptions<GitHubOptions> options)
{
    public async Task<IReadOnlyList<WorkflowRunSummary>> ListRunsAsync(
        string? owner, string? repo, string? branch, int? count, CancellationToken cancellationToken)
    {
        var repository = access.Resolve(owner, repo);
        var validBranch = GitHubInput.OptionalRef(branch, "branch");

        var path = $"{repository.ApiPath}/actions/runs?per_page={options.Value.ClampCount(count)}"
                   + (validBranch is null ? string.Empty : $"&branch={Uri.EscapeDataString(validBranch)}");

        var runs = await api.GetAsync<ApiWorkflowRuns>(path, cancellationToken);
        return [.. runs.WorkflowRuns.Select(GitHubMapper.ToWorkflowRunSummary)];
    }
}
