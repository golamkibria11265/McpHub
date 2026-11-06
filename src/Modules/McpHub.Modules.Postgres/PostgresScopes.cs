namespace McpHub.Modules.Postgres;

/// <summary>Scopes a caller's token must carry. Every tool in this module is read-only.</summary>
public static class PostgresScopes
{
    public const string Read = "postgres.read";
}
