using McpHub.Core.Auditing;
using McpHub.Core.Security;
using McpHub.Infrastructure.Auditing;
using McpHub.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace McpHub.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registers shared building blocks. Uses TryAdd so tests or hosts can substitute any of them.</summary>
    public static IServiceCollection AddMcpHubInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<ICredentialProvider, ConfigurationCredentialProvider>();
        services.TryAddSingleton<IAuditLog, LoggerAuditLog>();
        return services;
    }
}
