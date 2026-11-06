namespace McpHub.Modules.Mongo;

/// <summary>
/// One named MongoDB database. Only non-secret settings live here (<c>Modules:Mongo:Connections:{name}</c>);
/// the connection string is a credential, read from <c>Modules:Mongo:Credentials:Connections:{name}:ConnectionString</c>.
/// </summary>
public sealed class MongoConnectionOptions
{
    /// <summary>Shown to the model so it can pick the right database.</summary>
    public string? Description { get; set; }

    /// <summary>Database name, when it is not part of the connection string.</summary>
    public string? Database { get; set; }

    /// <summary>Allows insert and update through this connection. Off by default; the database user must also have the rights.</summary>
    public bool AllowWrite { get; set; }

    /// <summary>Allows delete through this connection. Only effective together with <see cref="AllowWrite"/>.</summary>
    public bool AllowDelete { get; set; }
}

/// <summary>Bound from <c>Modules:Mongo</c>.</summary>
public sealed class MongoOptions
{
    public Dictionary<string, MongoConnectionOptions> Connections { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Upper bound on documents returned per call.</summary>
    public int MaxRows { get; set; } = 200;

    /// <summary>Per-command timeout.</summary>
    public int CommandTimeoutSeconds { get; set; } = 15;

    /// <summary>Longest single MongoDB document (as JSON) returned; longer ones are replaced by a preview.</summary>
    public int MaxDocumentChars { get; set; } = 20_000;

    /// <summary>A write that would touch more documents than this is refused.</summary>
    public int MaxAffectedRows { get; set; } = 100;

    /// <summary>Extra field names never returned or queried (matching ignores case, '_' and '-').</summary>
    public string[] ExtraDeniedColumns { get; set; } = [];

    public int ClampRows(int? requested) => Math.Clamp(requested ?? MaxRows, 1, MaxRows);
}
