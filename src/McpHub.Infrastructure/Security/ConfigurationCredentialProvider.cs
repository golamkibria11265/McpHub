using McpHub.Core.Errors;
using McpHub.Core.Security;
using Microsoft.Extensions.Configuration;

namespace McpHub.Infrastructure.Security;

/// <summary>
/// Reads <c>Modules:{integration}:Credentials:Token</c> from configuration, so a module's credential sits next to the
/// rest of that module's settings. In production this comes from an environment variable
/// (<c>Modules__GitHub__Credentials__Token</c>) populated from your secret store, never from a file in the repository.
/// The older top-level <c>Credentials:{integration}:Token</c> location is still honoured as a fallback, so existing
/// settings files keep working until they are moved.
/// Swap this class for a vault-backed provider without touching any module.
/// </summary>
internal sealed class ConfigurationCredentialProvider(IConfiguration configuration) : ICredentialProvider
{
    public ValueTask<string> GetAsync(string integration, CancellationToken cancellationToken = default)
    {
        var token = configuration[$"Modules:{integration}:Credentials:Token"]
                    ?? configuration[$"Credentials:{integration}:Token"];

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
