using System.Text.Json.Nodes;

namespace McpHub.Modules.Mongo;

public sealed record DocumentResult(
    string Connection,
    string Collection,
    IReadOnlyList<JsonNode?> Documents,
    int DocumentCount,
    bool Truncated,
    int RedactedFields);
