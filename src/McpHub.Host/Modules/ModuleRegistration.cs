namespace McpHub.Host.Modules;

/// <summary>Names of the modules that were enabled at startup (for logging and diagnostics).</summary>
internal sealed record LoadedModules(IReadOnlyList<string> Names);

internal static class ModuleRegistration
{
    /// <summary>
    /// Registers every module switched on via <c>Modules:{Name}:Enabled</c>, and exposes its tools to MCP.
    /// Disabled modules are never configured, so their credentials and options are never read.
    /// </summary>
    public static IMcpServerBuilder AddIntegrationModules(
        this IMcpServerBuilder mcp,
        IServiceCollection services,
        IConfiguration configuration)
    {
        var enabled = new List<string>();

        foreach (var module in ModuleLoader.Discover())
        {
            if (!configuration.GetValue<bool>($"Modules:{module.Name}:Enabled"))
            {
                continue;
            }

            module.ConfigureServices(services, configuration);
            mcp.WithToolsFromAssembly(module.GetType().Assembly);
            enabled.Add(module.Name);
        }

        services.AddSingleton(new LoadedModules(enabled));
        return mcp;
    }
}
