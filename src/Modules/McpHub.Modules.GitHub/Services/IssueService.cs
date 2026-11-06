using McpHub.Modules.GitHub.Access;
using McpHub.Modules.GitHub.Clients;
using McpHub.Modules.GitHub.Models;
using McpHub.Modules.GitHub.Models.Api;
using Microsoft.Extensions.Options;

namespace McpHub.Modules.GitHub.Services;

/// <summary>Use case: search issues and pull requests inside one allowed repository.</summary>
public sealed class IssueService(
    IGitHubApiClient api,
    RepositoryAccessPolicy access,
    IOptions<GitHubOptions> options)
{
    public async Task<IssueSearchResult> SearchAsync(
        string? owner, string? repo, string? query, string? state, int? count, CancellationToken cancellationToken)
    {
        var repository = access.Resolve(owner, repo);
        var text = GitHubInput.RequireSearchText(query);
        var stateFilter = GitHubInput.ResolveState(state);

        // The repo: qualifier is added by the server, never by the caller.
        var q = $"{text} repo:{repository} {stateFilter}".Trim();
        var path = $"search/issues?q={Uri.EscapeDataString(q)}&sort=updated&per_page={options.Value.ClampCount(count)}";

        var result = await api.GetAsync<ApiSearchResult>(path, cancellationToken);
        return new IssueSearchResult(result.TotalCount, [.. result.Items.Select(GitHubMapper.ToIssueSummary)]);
    }
}
