namespace McpHub.Modules.Mongo;

public sealed record ConnectionInfo(string Name, string Provider, string? Description);

public sealed record ObjectInfo(string? Schema, string Name, string Type);

public sealed record ObjectList(string Connection, IReadOnlyList<ObjectInfo> Objects, bool Truncated);

/// <summary>A top-level field seen in sampled documents.</summary>
public sealed record ColumnInfo(string Name, string Type, bool Nullable, int? MaxLength, bool Restricted);

public sealed record ObjectDescription(string Connection, string? Schema, string Name, IReadOnlyList<ColumnInfo> Columns);

/// <summary>Outcome of a write. <see cref="Applied"/> is false for a preview, where nothing was changed.</summary>
public sealed record WriteResult(string Connection, string Operation, long Count, bool Applied, string Message);
