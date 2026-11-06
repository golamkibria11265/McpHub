namespace McpHub.Modules.Sql;

public sealed record ConnectionInfo(string Name, string Provider, string? Description);

public sealed record ObjectInfo(string? Schema, string Name, string Type);

/// <summary>Tables and views.</summary>
public sealed record ObjectList(string Connection, IReadOnlyList<ObjectInfo> Objects, bool Truncated);

public sealed record ColumnInfo(string Name, string Type, bool Nullable, int? MaxLength, bool Restricted);

public sealed record ObjectDescription(string Connection, string? Schema, string Name, IReadOnlyList<ColumnInfo> Columns);

public sealed record QueryResult(
    string Connection,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    int RowCount,
    bool Truncated,
    IReadOnlyList<string> RedactedColumns);
