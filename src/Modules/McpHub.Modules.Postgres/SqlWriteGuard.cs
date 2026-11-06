using System.Text.RegularExpressions;
using McpHub.Core.Errors;

namespace McpHub.Modules.Postgres;

public enum SqlStatementKind
{
    InsertOrUpdate,
    Delete
}

/// <summary>A checked write statement: the SQL to run, what it is, and every identifier it mentions.</summary>
public sealed record SqlWriteScan(string Sql, string Operation, IReadOnlyList<string> Identifiers);

/// <summary>
/// Accepts exactly one INSERT, UPDATE or DELETE statement and nothing else: no schema changes (DDL), no privileges,
/// no procedures, no comments, no second statement. UPDATE and DELETE must have a WHERE clause.
/// Like the read guard it is a shape filter, not a parser; the real limits are the database login's rights, the
/// row-count cap and the rollback preview (see <c>confirm</c>).
/// </summary>
public static class SqlWriteGuard
{
    private static readonly HashSet<string> Forbidden = new(StringComparer.Ordinal)
    {
        "DROP", "ALTER", "CREATE", "TRUNCATE", "RENAME", "GRANT", "REVOKE", "DENY", "EXEC", "EXECUTE", "CALL", "DO",
        "COPY", "LOCK", "DECLARE", "BEGIN", "COMMIT", "ROLLBACK", "SAVEPOINT", "USE", "MERGE", "UPSERT", "REPLACE",
        "ATTACH", "DETACH", "VACUUM", "ANALYZE", "REINDEX", "CLUSTER", "REFRESH", "LOAD", "PREPARE", "DEALLOCATE",
        "SHUTDOWN", "BACKUP", "RESTORE", "KILL", "BULK", "RETURNING"
    };

    private static readonly string[] ForbiddenPrefixes = ["XP_", "SP_", "DBLINK", "PG_", "LO_", "SET_CONFIG", "NEXTVAL", "SETVAL"];

    public static SqlWriteScan Ensure(string? sql, SqlStatementKind expected)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw SqlGuard.Invalid("'sql' is required.");
        }

        var text = sql.Trim();
        if (text.Length > 10_000)
        {
            throw SqlGuard.Invalid("The statement is longer than 10000 characters.");
        }

        var (code, quoted, trailingSemicolon) = SqlGuard.Mask(text);
        var words = SqlGuard.WordPattern().Matches(code).Select(m => m.Value).ToList();
        var first = words.Count > 0 ? words[0].ToUpperInvariant() : string.Empty;

        var operation = first switch
        {
            "INSERT" or "UPDATE" when expected == SqlStatementKind.InsertOrUpdate => first,
            "DELETE" when expected == SqlStatementKind.Delete => first,
            "DELETE" => throw SqlGuard.Invalid("DELETE statements must be sent with the delete tool."),
            "INSERT" or "UPDATE" => throw SqlGuard.Invalid("Send INSERT and UPDATE with the execute tool, not the delete tool."),
            _ => throw SqlGuard.Invalid(expected == SqlStatementKind.Delete
                ? "Only a single DELETE statement is allowed here."
                : "Only a single INSERT or UPDATE statement is allowed here.")
        };

        var upper = words.Select(w => w.ToUpperInvariant()).ToList();
        foreach (var word in upper)
        {
            if (Forbidden.Contains(word) || ForbiddenPrefixes.Any(p => word.StartsWith(p, StringComparison.Ordinal)))
            {
                throw SqlGuard.Invalid($"'{word}' is not allowed: only plain INSERT, UPDATE and DELETE statements can be run, no schema changes, procedures or system functions.");
            }
        }

        if (operation == "INSERT" ? upper.Count(w => w == "INTO") != 1 : upper.Contains("INTO"))
        {
            throw SqlGuard.Invalid("Use exactly one 'INSERT INTO ...' and no other INTO.");
        }

        if (operation != "INSERT" && !upper.Contains("WHERE"))
        {
            throw SqlGuard.Invalid($"{operation} must have a WHERE clause so it cannot change every row.");
        }

        if (operation == "UPDATE" && !upper.Contains("SET"))
        {
            throw SqlGuard.Invalid("UPDATE needs a SET clause.");
        }

        if (operation != "UPDATE" && upper.Contains("SET"))
        {
            throw SqlGuard.Invalid("'SET' is only allowed in an UPDATE.");
        }

        var finalSql = trailingSemicolon ? text[..^1].TrimEnd() : text;
        return new SqlWriteScan(finalSql, operation, [.. words, .. quoted]);
    }
}
