namespace McpHub.Modules.Telegram;

/// <summary>
/// The only code that speaks MTProto. Everything above it (access rules, limits, redaction) works on plain records,
/// so it can be tested without a Telegram account. Methods that take a <see cref="ChatRef"/> expect it to come from
/// <see cref="ResolveChatAsync"/>.
/// </summary>
public interface ITelegramGateway
{
    Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken);

    /// <summary>Accepts "me", a numeric chat id, or an @username.</summary>
    Task<ChatRef> ResolveChatAsync(string reference, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatSummary>> ListChatsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ContactInfo>> ListContactsAsync(CancellationToken cancellationToken);

    /// <summary>Newest first. <paramref name="beforeMessageId"/> of 0 starts from the latest message.</summary>
    Task<IReadOnlyList<MessageInfo>> GetMessagesAsync(ChatRef chat, int limit, int beforeMessageId, CancellationToken cancellationToken);

    /// <summary>Searches one chat, or every chat when <paramref name="chat"/> is null.</summary>
    Task<IReadOnlyList<MessageInfo>> SearchMessagesAsync(ChatRef? chat, string query, int limit, CancellationToken cancellationToken);

    Task<int> SendMessageAsync(ChatRef chat, string text, int? replyToMessageId, CancellationToken cancellationToken);

    Task<int> SendFileAsync(ChatRef chat, string fileName, byte[] content, string? caption, CancellationToken cancellationToken);

    Task EditMessageAsync(ChatRef chat, int messageId, string text, CancellationToken cancellationToken);

    Task ForwardMessagesAsync(ChatRef from, ChatRef to, IReadOnlyList<int> messageIds, CancellationToken cancellationToken);

    Task MarkReadAsync(ChatRef chat, CancellationToken cancellationToken);

    Task ReactAsync(ChatRef chat, int messageId, string emoji, CancellationToken cancellationToken);

    Task DeleteMessagesAsync(ChatRef chat, IReadOnlyList<int> messageIds, CancellationToken cancellationToken);
}
