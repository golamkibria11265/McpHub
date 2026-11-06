using System.Text.RegularExpressions;
using McpHub.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace McpHub.Modules.Sql;

/// <summary>
/// Read-only access to SQL Server through named connections.
/// Enable with <c>Modules:Sql:Enabled=true</c>; connection strings live under <c>Modules:Sql:Credentials:Connections</c>.
/// </summary>
public sealed partial class SqlModule : IIntegrationModule
{
    public const string IntegrationName = "Sql";

    public string Name => IntegrationName;

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,50}$")]
    private static partial Regex ConnectionNamePattern();

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Fails at startup (not on the first call) when the section is incomplete.
        services.AddOptions<SqlOptions>()
            .Bind(configuration.GetSection($"Modules:{Name}"))
            .Validate(o => o.Connections.Count > 0,
                "Modules:Sql:Connections must define at least one connection.")
            .Validate(o => o.Connections.Keys.All(k => ConnectionNamePattern().IsMatch(k)),
                "Connection names under Modules:Sql:Connections may only contain letters, digits, '_' and '-' (max 50).")
            .Validate(o => o.Connections.Keys.All(k => !string.IsNullOrWhiteSpace(configuration[ConnectionRegistry.ConnectionStringKey(k)])),
                "Every connection needs Modules:Sql:Credentials:Connections:{{name}}:ConnectionString.")
            .Validate(o => o.MaxRows is >= 1 and <= 5_000 && o.CommandTimeoutSeconds is >= 1 and <= 120 && o.MaxCellChars >= 50,
                "Modules:Sql limits are out of range (MaxRows 1-5000, CommandTimeoutSeconds 1-120, MaxCellChars >= 50).")
            .Validate(o => o.MaxAffectedRows is >= 1 and <= 10_000,
                "Modules:Sql:MaxAffectedRows must be between 1 and 10000.")
            .ValidateOnStart();

        services.AddSingleton<SqlDatabase>();
    }
}
