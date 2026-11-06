using McpHub.Core.Errors;
using McpHub.Modules.GitHub.Access;
using McpHub.Modules.GitHub.Models.Api;
using McpHub.Modules.GitHub.Services;

namespace McpHub.UnitTests.GitHub;

public class IssueServiceTests
{
    private static (IssueService Service, FakeGitHubApiClient Client) Create()
    {
        var client = new FakeGitHubApiClient(_ => new ApiSearchResult(0, []));
        var options = TestOptions.GitHub("acme");
        return (new IssueService(client, new RepositoryAccessPolicy(options), options), client);
    }

    [Theory]
    [InlineData("bug repo:other/private")]
    [InlineData("org:other login")]
    [InlineData("login USER:someone")]
    public async Task Search_rejects_scope_widening_qualifiers_without_calling_github(string query)
    {
        var (service, client) = Create();

        var ex = await Assert.ThrowsAsync<IntegrationException>(
            () => service.SearchAsync("acme", "portal", query, null, null, CancellationToken.None));

        Assert.Equal(IntegrationErrorKind.InvalidInput, ex.Kind);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Search_scopes_query_to_the_repository_and_state()
    {
        var (service, client) = Create();

        await service.SearchAsync("acme", "portal", "login bug", "open", 5, CancellationToken.None);

        var request = Assert.Single(client.Requests);
        Assert.Contains(Uri.EscapeDataString("repo:acme/portal"), request);
        Assert.Contains(Uri.EscapeDataString("is:open"), request);
        Assert.Contains("per_page=5", request);
    }

    [Fact]
    public async Task Search_caps_requested_count_at_the_configured_maximum()
    {
        var (service, client) = Create();

        await service.SearchAsync("acme", "portal", "bug", null, 10_000, CancellationToken.None);

        Assert.Contains("per_page=30", Assert.Single(client.Requests));
    }

    [Fact]
    public async Task Search_rejects_unknown_state()
    {
        var (service, _) = Create();

        await Assert.ThrowsAsync<IntegrationException>(
            () => service.SearchAsync("acme", "portal", "bug", "weird", null, CancellationToken.None));
    }
}
