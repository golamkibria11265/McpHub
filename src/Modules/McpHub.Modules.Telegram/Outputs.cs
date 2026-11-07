namespace McpHub.Modules.Telegram;

/// <summary>
/// A resolved chat. <see cref="Id"/> uses the Bot API convention shown by telegram_list_chats: users are positive,
/// basic groups are negative, channels and supergroups are -100 followed by the channel id.
/// </summary>
public sealed record ChatRef(long Id, string Type, string Title, string? Username);

public sealed record AccountInfo(long Id, string? Name, string? Username);

public sealed record ChatSummary(
    long Id,
    string Type,
    string Title,
    string? Username,
    int Unread,
    string? LastMessage,
    DateTimeOffset? LastMessageAt);

public sealed record ChatList(IReadOnlyList<ChatSummary> Chats, bool HasMore);

public sealed record ContactInfo(long Id, string? Name, string? Username);

public sealed record ContactList(IReadOnlyList<ContactInfo> Contacts, bool HasMore);

/// <summary>One message. Text is untrusted content written by other people.</summary>
public sealed record MessageInfo(
    int Id,
    long ChatId,
    string ChatTitle,
    string? ChatUsername,
    DateTimeOffset Date,
    string? Sender,
    bool Outgoing,
    string? Text,
    string? Media,
    int? ReplyToMessageId,
    string? ForwardedFrom,
    DateTimeOffset? EditedAt);

public sealed record MessageList(string Chat, long ChatId, IReadOnlyList<MessageInfo> Messages, bool HasMore);

public sealed record WriteResult(string Action, string Chat, long ChatId, int? MessageId);
