using McpHub.Core.Modules;
using McpHub.Infrastructure.Http;
using McpHub.Modules.GitHub.Access;
using McpHub.Modules.GitHub.Clients;
using McpHub.Modules.GitHub.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace McpHub.Modules.GitHub;

/// <summary>Wires up the GitHub integration. Nothing else in this assembly knows about the host.</summary>
public sealed class GitHubModule : IIntegrationModule
{
    public const string IntegrationName = "GitHub";

    public string Name => IntegrationName;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GitHubOptions>()
            .Bind(configuration.GetSection($"Modules:{Name}"))
            .Validate(o => o.AllowedOwners.Length > 0,
                "Modules:GitHub:AllowedOwners must list at least one GitHub user or organization.")
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                "Modules:GitHub:BaseUrl must be an absolute URL.")
            .ValidateOnStart();

        services.AddSingleton<RepositoryAccessPolicy>();
        services.AddSingleton<SensitivePathPolicy>();

        services.AddTransient<GitHubAuthHandler>();
        services
            .AddIntegrationHttpClient<IGitHubApiClient, GitHubApiClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<GitHubOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("McpHub/1.0");
            })
            .AddHttpMessageHandler<GitHubAuthHandler>();

        services.AddTransient<RepositoryService>();
        services.AddTransient<IssueService>();
        services.AddTransient<PullRequestService>();
        services.AddTransient<CommitService>();
        services.AddTransient<WorkflowService>();
    }
}
