using System.Text.Json.Nodes;

namespace McpHub.Modules.Postman;

public sealed record WorkspaceInfo(string Id, string? Name, string? Type, string? Visibility);

public sealed record ItemInfo(string Uid, string? Name, string? UpdatedAt);

public sealed record ItemList(string Workspace, IReadOnlyList<ItemInfo> Items, bool Truncated);

/// <summary>A collection as JSON, or its outline. Secret values are replaced by a mask.</summary>
public sealed record CollectionResult(string Uid, string? Name, bool Outline, int MaskedValues, JsonNode? Content);

public sealed record EnvironmentVariable(string Key, string? Value, string Type, bool Enabled, bool Masked);

public sealed record EnvironmentResult(string Uid, string? Name, IReadOnlyList<EnvironmentVariable> Variables, int MaskedValues);

public sealed record WriteResult(string Action, string Uid, string? Name);

/// <summary>Variable to set on an environment. Secrets cannot be written through this server; use a {{reference}}.</summary>
public sealed class EnvironmentVariableInput
{
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    public bool? Enabled { get; set; }
}
