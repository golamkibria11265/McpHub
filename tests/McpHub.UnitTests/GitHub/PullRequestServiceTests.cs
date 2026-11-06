using McpHub.Core.Errors;
using McpHub.Modules.GitHub.Access;
using McpHub.Modules.GitHub.Models.Api;
using McpHub.Modules.GitHub.Services;

namespace McpHub.UnitTests.GitHub;

public class PullRequestServiceTests
{
    private static PullRequestService Create(FakeGitHubApiClient client) =>
        new(client, new RepositoryAccessPolicy(TestOptions.GitHub("acme")));

    [Fact]
    public async Task Get_returns_compact_details_with_changed_files()
    {
        var now = DateTimeOffset.UtcNow;
        var client = new FakeGitHubApiClient(path => path.Contains("/files", StringComparison.Ordinal)
            ? new List<ApiPullFile> { new("src/A.cs", "modified", 10, 2) }
            : new ApiPullRequest(7, "Fix login", "open", false, false, new ApiUser("dev1"),
                new ApiBranchRef("feature/login"), new ApiBranchRef("main"), "body",
                10, 2, 1, 3, now, now, "https://github.com/acme/portal/pull/7"));

        var details = await Create(client).GetAsync("acme", "portal", 7, CancellationToken.None);

        Assert.Equal("Fix login", details.Title);
        Assert.Equal("feature/login", details.HeadBranch);
        Assert.Equal("main", details.BaseBranch);
        var file = Assert.Single(details.Files);
        Assert.Equal("src/A.cs", file.Path);
        Assert.Equal(10, file.Additions);
    }

    [Fact]
    public async Task Get_rejects_non_positive_number()
    {
        var client = new FakeGitHubApiClient(_ => throw new InvalidOperationException("must not be called"));

        var ex = await Assert.ThrowsAsync<IntegrationException>(
            () => Create(client).GetAsync("acme", "portal", 0, CancellationToken.None));

        Assert.Equal(IntegrationErrorKind.InvalidInput, ex.Kind);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Get_rejects_repository_of_a_non_allowed_owner()
    {
        var client = new FakeGitHubApiClient(_ => throw new InvalidOperationException("must not be called"));

        var ex = await Assert.ThrowsAsync<IntegrationException>(
            () => Create(client).GetAsync("evil-corp", "portal", 1, CancellationToken.None));

        Assert.Equal(IntegrationErrorKind.Forbidden, ex.Kind);
    }
}
