using System.Text.RegularExpressions;
using McpHub.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace McpHub.Modules.Mongo;

/// <summary>
/// Read-only access to MongoDB through named connections.
/// Enable with <c>Modules:Mongo:Enabled=true</c>; connection strings live under <c>Modules:Mongo:Credentials:Connections</c>.
/// </summary>
public sealed partial class MongoModule : IIntegrationModule
{
    public const string IntegrationName = "Mongo";

    public string Name => IntegrationName;

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,50}$")]
    private static partial Regex ConnectionNamePattern();

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MongoOptions>()
            .Bind(configuration.GetSection($"Modules:{Name}"))
            .Validate(o => o.Connections.Count > 0,
                "Modules:Mongo:Connections must define at least one connection.")
            .Validate(o => o.Connections.Keys.All(k => ConnectionNamePattern().IsMatch(k)),
                "Connection names under Modules:Mongo:Connections may only contain letters, digits, '_' and '-' (max 50).")
            .Validate(o => o.Connections.Keys.All(k => !string.IsNullOrWhiteSpace(configuration[ConnectionRegistry.ConnectionStringKey(k)])),
                "Every connection needs Modules:Mongo:Credentials:Connections:{name}:ConnectionString.")
            .Validate(o => o.MaxRows is >= 1 and <= 5_000 && o.CommandTimeoutSeconds is >= 1 and <= 120 && o.MaxDocumentChars >= 200,
                "Modules:Mongo limits are out of range (MaxRows 1-5000, CommandTimeoutSeconds 1-120, MaxDocumentChars >= 200).")
            .ValidateOnStart();

        // Singleton: the MongoDB client is designed to be created once and shared.
        services.AddSingleton<MongoService>();
    }
}
