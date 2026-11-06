namespace McpHub.Modules.GitHub.Clients;

/// <summary>
/// The only seam to the GitHub REST API. Services depend on this interface, so they are tested with a fake
/// and the HTTP details (auth, retries, error mapping) stay in one implementation.
/// </summary>
public interface IGitHubApiClient
{
    /// <param name="relativePath">Path relative to the API base address, without a leading slash.</param>
    /// <exception cref="McpHub.Core.Errors.IntegrationException">Mapped, user-safe failure.</exception>
    Task<T> GetAsync<T>(string relativePath, CancellationToken cancellationToken);
}
