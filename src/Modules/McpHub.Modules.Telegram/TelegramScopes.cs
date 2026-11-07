namespace McpHub.Modules.Telegram;

/// <summary>Scopes a caller's token must carry. Writes and deletes are separate so they can be granted on their own.</summary>
public static class TelegramScopes
{
    public const string Read = "telegram.read";
    public const string Write = "telegram.write";
    public const string Delete = "telegram.delete";
}
