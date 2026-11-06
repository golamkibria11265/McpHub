namespace McpHub.Modules.Mongo;

/// <summary>Scopes a caller's token must carry. Every tool in this module is read-only.</summary>
public static class MongoScopes
{
    public const string Read = "mongo.read";
}
