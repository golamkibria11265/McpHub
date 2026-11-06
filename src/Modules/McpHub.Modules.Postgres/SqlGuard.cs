using System.Text;
using System.Text.RegularExpressions;
using McpHub.Core.Errors;

namespace McpHub.Modules.Postgres;

/// <summary>What the guard found in a query: the SQL to run and every identifier it mentions.</summary>
public sealed record SqlScan(string Sql, IReadOnlyList<string> Identifiers);

/// <summary>
/// First line of defence for model-written PostgreSQL SQL: accepts exactly one SELECT (or WITH ... SELECT) statement and
/// rejects everything that can write, lock, wait or reach outside the database.
/// </summary>
/// <remarks>
/// This is a deny-by-shape filter, not a SQL parser, so it is deliberately strict (no comments, no multiple
/// statements, no dollar-quoting). It is NOT the only protection: connect with an account that only has SELECT rights; the session is also switched to read-only.
/// </remarks>
public static partial class SqlGuard
{
    private const int MaxLength = 10_000;

    [GeneratedRegex("[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"\bFOR\s+(NO\s+KEY\s+)?(SHARE|KEY\s+SHARE)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LockingPattern();

    private static readonly HashSet<string> ForbiddenWords = new(StringComparer.Ordinal)
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "UPSERT", "REPLACE", "DROP", "ALTER", "CREATE", "TRUNCATE", "RENAME",
        "GRANT", "REVOKE", "DENY", "EXEC", "EXECUTE", "CALL", "DO", "INTO", "COPY", "LOCK", "SET", "DECLARE", "BEGIN",
        "COMMIT", "ROLLBACK", "SAVEPOINT", "USE", "ATTACH", "DETACH", "VACUUM", "ANALYZE", "REINDEX", "CLUSTER",
        "REFRESH", "LOAD", "PREPARE", "DEALLOCATE", "SHUTDOWN", "BACKUP", "RESTORE", "KILL", "BULK",
        "NEXTVAL", "SETVAL", "SET_CONFIG", "LO_IMPORT", "LO_EXPORT", "LO_GET", "LO_PUT", "LO_CREATE", "LO_UNLINK"
    };

    private static readonly string[] ForbiddenPrefixes =
    [
        "DBLINK", "PG_READ", "PG_LS_", "PG_STAT_FILE", "PG_ADVISORY", "PG_TERMINATE", "PG_CANCEL", "PG_RELOAD", "PG_SLEEP", "PG_FILE", "PG_LOGICAL", "PG_REPLICATION"
    ];

    public static SqlScan EnsureReadOnly(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw Invalid("'sql' is required.");
        }

        var text = sql.Trim();
        if (text.Length > MaxLength)
        {
            throw Invalid($"The query is longer than {MaxLength} characters.");
        }

        var (code, quoted, hadTrailingSemicolon) = Mask(text);

        var words = WordPattern().Matches(code).Select(m => m.Value).ToList();
        var first = code.TrimStart('(', ' ', '\t', '\r', '\n');
        if (words.Count == 0 || !(first.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                                  || first.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)))
        {
            throw Invalid("Only a single SELECT statement is allowed (it may start with WITH).");
        }

        foreach (var word in words)
        {
            var upper = word.ToUpperInvariant();
            if (ForbiddenWords.Contains(upper) || ForbiddenPrefixes.Any(p => upper.StartsWith(p, StringComparison.Ordinal)))
            {
                throw Invalid($"'{word}' is not allowed: this connection is read-only and cannot use write, lock or system operations.");
            }
        }

        if (LockingPattern().IsMatch(code))
        {
            throw Invalid("Locking reads and sequence access are not allowed.");
        }

        var finalSql = hadTrailingSemicolon ? text[..^1].TrimEnd() : text;
        return new SqlScan(finalSql, [.. words, .. quoted]);
    }

    /// <summary>
    /// Replaces string literals and quoted identifiers by blanks so keyword checks only see real SQL, and refuses
    /// anything that could hide a second statement (comments, dollar-quoting, a ';' that is not the last character).
    /// Backslash is intentionally NOT treated as an escape: engines disagree about it, and the strict reading can
    /// only produce a false rejection, never a hidden statement.
    /// </summary>
    private static (string Code, List<string> Quoted, bool TrailingSemicolon) Mask(string text)
    {
        var code = new StringBuilder(text.Length);
        var quoted = new List<string>();
        var trailingSemicolon = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (c < ' ' && c is not ('\t' or '\n' or '\r'))
            {
                throw Invalid("The query contains control characters.");
            }

            switch (c)
            {
                case '\'':
                    i = ReadQuoted(text, i, '\'', out _);
                    code.Append(' ');
                    break;
                case '"':
                case '`':
                    i = ReadQuoted(text, i, c, out var identifier);
                    quoted.Add(identifier);
                    code.Append(' ');
                    break;
                case '-' when next == '-':
                case '/' when next == '*':
                case '#':
                    throw Invalid("Comments are not allowed in queries.");
                case '$' when next == '$' || char.IsLetter(next) || next == '_':
                    throw Invalid("Dollar-quoted strings are not allowed in queries.");
                case ';':
                    if (i != text.Length - 1)
                    {
                        throw Invalid("Only one statement is allowed; remove the ';'.");
                    }

                    trailingSemicolon = true;
                    break;
                default:
                    code.Append(c);
                    break;
            }
        }

        return (code.ToString(), quoted, trailingSemicolon);
    }

    /// <summary>Returns the index of the closing quote. A doubled closing quote is an escaped quote.</summary>
    private static int ReadQuoted(string text, int start, char close, out string content)
    {
        var sb = new StringBuilder();

        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] == close)
            {
                if (i + 1 < text.Length && text[i + 1] == close)
                {
                    sb.Append(close);
                    i++;
                    continue;
                }

                content = sb.ToString();
                return i;
            }

            sb.Append(text[i]);
        }

        throw Invalid("The query has an unterminated quote.");
    }

    private static IntegrationException Invalid(string message) =>
        new(IntegrationErrorKind.InvalidInput, PostgresModule.IntegrationName, message);
}
