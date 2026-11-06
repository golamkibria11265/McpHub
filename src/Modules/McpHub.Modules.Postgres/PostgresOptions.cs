namespace McpHub.Modules.Postgres;

/// <summary>
/// One named PostgreSQL database. Only non-secret settings live here (<c>Modules:Postgres:Connections:{name}</c>);
/// the connection string is a credential and is read from <c>Modules:Postgres:Credentials:Connections:{name}:ConnectionString</c>.
/// </summary>
public sealed class PostgresConnectionOptions
{
    /// <summary>Shown to the model so it can pick the right database.</summary>
    public string? Description { get; set; }
}

/// <summary>Bound from <c>Modules:Postgres</c>.</summary>
public sealed class PostgresOptions
{
    public Dictionary<string, PostgresConnectionOptions> Connections { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Upper bound on rows returned per call. Keeps responses (and LLM token cost) small.</summary>
    public int MaxRows { get; set; } = 200;

    /// <summary>Per-command timeout, so a heavy query cannot hold a connection or the server.</summary>
    public int CommandTimeoutSeconds { get; set; } = 15;

    /// <summary>Longest single text value returned; longer values are truncated.</summary>
    public int MaxCellChars { get; set; } = 2_000;

    /// <summary>
    /// Extra column names that are never returned or queried, on top of the built-in list
    /// (passwords, tokens, card numbers, CVV, PIN, ...). Matching ignores case, '_' and '-'.
    /// </summary>
    public string[] ExtraDeniedColumns { get; set; } = [];

    public int ClampRows(int? requested) => Math.Clamp(requested ?? MaxRows, 1, MaxRows);
}
