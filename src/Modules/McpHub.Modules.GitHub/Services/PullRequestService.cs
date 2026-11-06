using McpHub.Core.Errors;
using McpHub.Modules.GitHub.Access;
using McpHub.Modules.GitHub.Clients;
using McpHub.Modules.GitHub.Models;
using McpHub.Modules.GitHub.Models.Api;

namespace McpHub.Modules.GitHub.Services;

/// <summary>Use case: a pull request with its list of changed files (no diffs, to keep output small).</summary>
public sealed class PullRequestService(IGitHubApiClient api, RepositoryAccessPolicy access)
{
    private const int MaxFiles = 100;

    public async Task<PullRequestDetails> GetAsync(string? owner, string? repo, int number, CancellationToken cancellationToken)
    {
        var repository = access.Resolve(owner, repo);

        if (number <= 0)
        {
            throw new IntegrationException(
                IntegrationErrorKind.InvalidInput, GitHubModule.IntegrationName, "'number' must be a positive integer.");
        }

        var basePath = $"{repository.ApiPath}/pulls/{number}";
        var pullRequest = api.GetAsync<ApiPullRequest>(basePath, cancellationToken);
        var files = api.GetAsync<List<ApiPullFile>>($"{basePath}/files?per_page={MaxFiles}", cancellationToken);

        await Task.WhenAll(pullRequest, files);

        return GitHubMapper.ToPullRequestDetails(await pullRequest, await files);
    }
}
