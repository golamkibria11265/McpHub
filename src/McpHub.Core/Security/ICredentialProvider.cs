namespace McpHub.Core.Security;

/// <summary>
/// Single place where integrations obtain credentials for outbound calls.
/// Today: configuration (environment variables / secret store). Later: a vault, or OAuth refresh-token flows
/// (LinkedIn, Facebook) behind the same interface, so modules never change.
/// </summary>
public interface ICredentialProvider
{
    /// <param name="integration">Integration name, e.g. "GitHub".</param>
    ValueTask<string> GetAsync(string integration, CancellationToken cancellationToken = default);
}
