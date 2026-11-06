using McpHub.Core.Errors;
using McpHub.Core.Security;
using Microsoft.Extensions.Configuration;

namespace McpHub.Infrastructure.Security;

/// <summary>
/// Reads <c>Credentials:{integration}:Token</c> from configuration. In production this comes from an environment
/// variable (<c>Credentials__GitHub__Token</c>) populated from your secret store, never from a file in the repository.
/// Swap this class for a vault-backed provider without touching any module.
/// </summary>
internal sealed class ConfigurationCredentialProvider(IConfiguration configuration) : ICredentialProvider
{
    public ValueTask<string> GetAsync(string integration, CancellationToken cancellationToken = default)
    {
        var token = configuration[$"Credentials:{integration}:Token"];

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new IntegrationException(
                IntegrationErrorKind.Unauthorized,
                integration,
                "The server has no credential configured for this integration. Contact the administrator.");
        }

        return ValueTask.FromResult(token);
    }
}
