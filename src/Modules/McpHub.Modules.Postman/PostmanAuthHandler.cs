using McpHub.Core.Security;

namespace McpHub.Modules.Postman;

/// <summary>Attaches the Postman API key to each outbound request. The key is never stored on the client.</summary>
internal sealed class PostmanAuthHandler(ICredentialProvider credentials) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = await credentials.GetAsync(PostmanModule.IntegrationName, cancellationToken);
        request.Headers.Remove("X-Api-Key");
        request.Headers.Add("X-Api-Key", key);
        return await base.SendAsync(request, cancellationToken);
    }
}
