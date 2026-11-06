using System.Text.Json.Nodes;

namespace McpHub.Modules.GitHub.Clients;

/// <summary>
/// Write seam to the GitHub REST API (POST/PUT/PATCH/DELETE). Kept apart from <see cref="IGitHubApiClient"/> so the
/// read-only services cannot reach it. Requests are never retried automatically.
/// </summary>
public interface IGitHubWriteClient
{
    /// <param name="relativePath">Path relative to the API base address, without a leading slash.</param>
    /// <exception cref="McpHub.Core.Errors.IntegrationException">Mapped, user-safe failure.</exception>
    Task<JsonNode> SendAsync(HttpMethod method, string relativePath, JsonNode? body, CancellationToken cancellationToken);
}
