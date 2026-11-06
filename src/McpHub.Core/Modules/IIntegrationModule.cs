using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace McpHub.Core.Modules;

/// <summary>
/// Entry point of an integration (GitHub, Jira, SQL Server, ...). The host discovers every implementation
/// in assemblies named <c>McpHub.Modules.*.dll</c>, so adding an integration never requires editing the host.
/// </summary>
/// <remarks>
/// A module is enabled only when <c>Modules:{Name}:Enabled</c> is <c>true</c> in configuration
/// (secure by default: nothing is exposed unless explicitly switched on).
/// MCP tools are found automatically: any class marked <c>[McpServerToolType]</c> in the module's assembly is registered.
/// </remarks>
public interface IIntegrationModule
{
    /// <summary>Unique name. Also the configuration key under <c>Modules</c> and the credential name.</summary>
    string Name { get; }

    /// <summary>Registers the module's own services (clients, services, options).</summary>
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}
