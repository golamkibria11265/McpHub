namespace McpHub.Modules.Postman;

/// <summary>Scopes a caller's token must carry. Writes and deletes are separate so they can be granted on their own.</summary>
public static class PostmanScopes
{
    public const string Read = "postman.read";
    public const string Write = "postman.write";
    public const string Delete = "postman.delete";
}
