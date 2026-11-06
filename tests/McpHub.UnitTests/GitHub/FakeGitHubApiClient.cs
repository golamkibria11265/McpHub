using McpHub.Modules.GitHub;
using McpHub.Modules.GitHub.Clients;
using Microsoft.Extensions.Options;

namespace McpHub.UnitTests.GitHub;

/// <summary>Records requested paths and answers from a delegate, so services are tested without HTTP.</summary>
internal sealed class FakeGitHubApiClient(Func<string, object> respond) : IGitHubApiClient
{
    public List<string> Requests { get; } = [];

    public Task<T> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        Requests.Add(relativePath);
        return Task.FromResult((T)respond(relativePath));
    }
}

internal static class TestOptions
{
    public static IOptions<GitHubOptions> GitHub(params string[] allowedOwners) =>
        Options.Create(new GitHubOptions { AllowedOwners = allowedOwners });
}
