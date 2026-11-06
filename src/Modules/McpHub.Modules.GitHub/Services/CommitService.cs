using McpHub.Modules.GitHub.Access;
using McpHub.Modules.GitHub.Clients;
using McpHub.Modules.GitHub.Models;
using McpHub.Modules.GitHub.Models.Api;
using Microsoft.Extensions.Options;

namespace McpHub.Modules.GitHub.Services;

/// <summary>Use case: recent commits of a repository, optionally on one branch.</summary>
public sealed class CommitService(
    IGitHubApiClient api,
    RepositoryAccessPolicy access,
    IOptions<GitHubOptions> options)
{
    public async Task<IReadOnlyList<CommitSummary>> ListAsync(
        string? owner, string? repo, string? branch, int? count, CancellationToken cancellationToken)
    {
        var repository = access.Resolve(owner, repo);
        var validBranch = GitHubInput.OptionalRef(branch, "branch");

        var path = $"{repository.ApiPath}/commits?per_page={options.Value.ClampCount(count)}"
                   + (validBranch is null ? string.Empty : $"&sha={Uri.EscapeDataString(validBranch)}");

        var commits = await api.GetAsync<List<ApiCommit>>(path, cancellationToken);
        return [.. commits.Select(GitHubMapper.ToCommitSummary)];
    }
}
