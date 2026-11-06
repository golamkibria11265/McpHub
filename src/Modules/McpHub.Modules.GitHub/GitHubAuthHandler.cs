using System.Net.Http.Headers;
using McpHub.Core.Security;

namespace McpHub.Modules.GitHub;

/// <summary>Attaches the GitHub credential to each outbound request. The token is never stored on the client.</summary>
internal sealed class GitHubAuthHandler(ICredentialProvider credentials) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await credentials.GetAsync(GitHubModule.IntegrationName, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
