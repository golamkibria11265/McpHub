namespace McpHub.Modules.Sql;

/// <summary>Scopes a caller's token must carry. Every tool in this module is read-only.</summary>
public static class SqlScopes
{
    public const string Read = "sql.read";
}
