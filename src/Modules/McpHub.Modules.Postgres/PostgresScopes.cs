namespace McpHub.Modules.Postgres;

/// <summary>Scopes a caller's token must carry. Reads, writes and deletes have separate scopes.</summary>
public static class PostgresScopes
{
    public const string Read = "postgres.read";
    public const string Write = "postgres.write";
    public const string Delete = "postgres.delete";
}
