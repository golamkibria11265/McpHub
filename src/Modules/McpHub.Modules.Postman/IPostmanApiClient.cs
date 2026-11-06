using System.Text.Json.Nodes;

namespace McpHub.Modules.Postman;

/// <summary>
/// The only seam to the Postman API. Services depend on this interface, so they are tested with a fake and the HTTP
/// details (API key, retries, error mapping) stay in one implementation.
/// </summary>
public interface IPostmanApiClient
{
    /// <param name="path">Path relative to the API base address, without a leading slash.</param>
    /// <exception cref="McpHub.Core.Errors.IntegrationException">Mapped, user-safe failure.</exception>
    Task<JsonNode> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken);
}
