using McpHub.Core.Errors;
using Microsoft.Extensions.Configuration;

namespace McpHub.Modules.Mongo;

/// <summary>A configured database, with its secret connection string. Never serialize or log this.</summary>
public sealed record ResolvedConnection(string Name, string ConnectionString, string? Database);

/// <summary>
/// Maps a connection NAME (the only thing the model ever sees) to a connection string read from
/// <c>Modules:Mongo:Credentials:Connections:{name}:ConnectionString</c>.
/// </summary>
public sealed class ConnectionRegistry(MongoOptions options, IConfiguration configuration)
{
    public static string ConnectionStringKey(string name) =>
        $"Modules:{MongoModule.IntegrationName}:Credentials:Connections:{name}:ConnectionString";

    public IReadOnlyList<ConnectionInfo> List() =>
    [
        .. options.Connections
            .OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
            .Select(c => new ConnectionInfo(c.Key, "MongoDb", c.Value.Description))
    ];

    public ResolvedConnection Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw Error(IntegrationErrorKind.InvalidInput, "'connection' is required. List the connections first to see the names.");
        }

        var match = options.Connections.FirstOrDefault(c => string.Equals(c.Key, name, StringComparison.OrdinalIgnoreCase));
        if (match.Value is null)
        {
            throw Error(IntegrationErrorKind.InvalidInput, "Unknown connection. List the connections first to see the available names.");
        }

        var connectionString = configuration[ConnectionStringKey(match.Key)];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw Error(IntegrationErrorKind.Unavailable, "This connection has no connection string configured. Contact the administrator.");
        }

        return new ResolvedConnection(match.Key, connectionString, match.Value.Database);
    }

    private static IntegrationException Error(IntegrationErrorKind kind, string message) =>
        new(kind, MongoModule.IntegrationName, message);
}
