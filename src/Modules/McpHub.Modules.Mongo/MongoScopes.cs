namespace McpHub.Modules.Mongo;

/// <summary>Scopes a caller's token must carry. Reads, writes and deletes have separate scopes.</summary>
public static class MongoScopes
{
    public const string Read = "mongo.read";
    public const string Write = "mongo.write";
    public const string Delete = "mongo.delete";
}
