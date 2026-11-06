namespace McpHub.Modules.Sql;

/// <summary>Scopes a caller's token must carry. Reads, writes and deletes have separate scopes.</summary>
public static class SqlScopes
{
    public const string Read = "sql.read";
    public const string Write = "sql.write";
    public const string Delete = "sql.delete";
}
