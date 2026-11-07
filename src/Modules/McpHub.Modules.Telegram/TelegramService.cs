using McpHub.Core.Errors;
using Microsoft.Extensions.Options;

namespace McpHub.Modules.Telegram;

/// <summary>All the rules (access, limits, redaction) in one place. The gateway only talks to Telegram.</summary>
public sealed class TelegramService(ITelegramGateway gateway, TelegramAccess access, IOptions<TelegramOptions> options)
{
    private const int MaxTelegramTextLength = 4096;
    private const int MaxCaptionLength = 1024;
    private const int MaxMessageIdsPerCall = 100;

    private TelegramOptions Options => options.Value;

    // ---------- reads ----------

    public Task<AccountInfo> GetAccountAsync(CancellationToken ct) => gateway.GetAccountAsync(ct);

    public async Task<ChatList> ListChatsAsync(string? query, string? type, int? count, CancellationToken ct)
    {
        var limit = Options.ClampCount(count);
        var q = query?.Trim();
        var t = type?.Trim();

        var matches = (await gateway.ListChatsAsync(ct))
            .Where(c => access.IsPermitted(c.Id, c.Username))
            .Where(c => string.IsNullOrEmpty(t) || string.Equals(c.Type, t, StringComparison.OrdinalIgnoreCase))
            .Where(c => string.IsNullOrEmpty(q)
                        || c.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || (c.Username?.Contains(q.TrimStart('@'), StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        return new ChatList(matches.Take(limit).Select(c => c with { LastMessage = Clean(c.LastMessage) }).ToList(), matches.Count > limit);
    }

    public async Task<ContactList> ListContactsAsync(string? query, int? count, CancellationToken ct)
    {
        var limit = Options.ClampCount(count);
        var q = query?.Trim();

        var matches = (await gateway.ListContactsAsync(ct))
            .Where(c => access.IsPermitted(c.Id, c.Username))
            .Where(c => string.IsNullOrEmpty(q)
                        || (c.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (c.Username?.Contains(q.TrimStart('@'), StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        return new ContactList(matches.Take(limit).ToList(), matches.Count > limit);
    }

    public async Task<MessageList> GetMessagesAsync(string chat, int? count, int? beforeMessageId, CancellationToken ct)
    {
        var limit = Options.ClampCount(count);
        var target = await ResolveAsync(chat, ct);
        if (beforeMessageId is < 0)
        {
            throw Fail(IntegrationErrorKind.InvalidInput, "beforeMessageId must be a positive message id.");
        }

        var raw = await gateway.GetMessagesAsync(target, limit + 1, beforeMessageId ?? 0, ct);
        return new MessageList(target.Title, target.Id, raw.Take(limit).Select(CleanMessage).ToList(), raw.Count > limit);
    }

    public async Task<MessageList> SearchMessagesAsync(string query, string? chat, int? count, CancellationToken ct)
    {
        var text = query?.Trim() ?? string.Empty;
        if (text.Length is 0 or > 200)
        {
            throw Fail(IntegrationErrorKind.InvalidInput, "A search text of up to 200 characters is required.");
        }

        var limit = Options.ClampCount(count);
        ChatRef? target = string.IsNullOrWhiteSpace(chat) ? null : await ResolveAsync(chat, ct);

        // Global search can return hits from blocked chats, so ask for a few extra and filter them out.
        var raw = await gateway.SearchMessagesAsync(target, text, target is null ? Math.Min(limit * 2, 100) : limit + 1, ct);
        var allowed = raw.Where(m => target is not null || access.IsPermitted(m.ChatId, m.ChatUsername)).ToList();
        return new MessageList(target?.Title ?? "all chats", target?.Id ?? 0, allowed.Take(limit).Select(CleanMessage).ToList(), allowed.Count > limit);
    }

    // ---------- writes ----------

    public async Task<WriteResult> SendMessageAsync(string chat, string text, int? replyToMessageId, CancellationToken ct)
    {
        access.RequireWrite();
        var target = await ResolveAsync(chat, ct);
        var body = ValidateText(text, MaxTelegramTextLength);
        if (replyToMessageId is <= 0)
        {
            throw Fail(IntegrationErrorKind.InvalidInput, "replyToMessageId must be a positive message id.");
        }

        access.AcquireWriteSlot();
        var id = await gateway.SendMessageAsync(target, body, replyToMessageId, ct);
        return new WriteResult("sent", target.Title, target.Id, id);
    }

    public async Task<WriteResult> EditMessageAsync(string chat, int messageId, string text, CancellationToken ct)
    {
        access.RequireWrite();
        var target = await ResolveAsync(chat, ct);
        var body = ValidateText(text, MaxTelegramTextLength);
        RequireMessageId(messageId);

        access.AcquireWriteSlot();
        await gateway.EditMessageAsync(target, messageId, body, ct);
        return new WriteResult("edited", target.Title, target.Id, messageId);
    }

    public async Task<WriteResult> ForwardMessagesAsync(string fromChat, string toChat, IReadOnlyList<int> messageIds, CancellationToken ct)
    {
        access.RequireWrite();
        var from = await ResolveAsync(fromChat, ct);
        var to = await ResolveAsync(toChat, ct);
        var ids = ValidateIds(messageIds);

        access.AcquireWriteSlot();
        await gateway.ForwardMessagesAsync(from, to, ids, ct);
        return new WriteResult("forwarded", to.Title, to.Id, null);
    }

    public async Task<WriteResult> MarkReadAsync(string chat, CancellationToken ct)
    {
        access.RequireWrite();
        var target = await ResolveAsync(chat, ct);

        access.AcquireWriteSlot();
        await gateway.MarkReadAsync(target, ct);
        return new WriteResult("marked read", target.Title, target.Id, null);
    }

    public async Task<WriteResult> ReactAsync(string chat, int messageId, string emoji, CancellationToken ct)
    {
        access.RequireWrite();
        var target = await ResolveAsync(chat, ct);
        RequireMessageId(messageId);
        var reaction = emoji?.Trim() ?? string.Empty;
        if (reaction.Length is 0 or > 16)
        {
            throw Fail(IntegrationErrorKind.InvalidInput, "Give a single emoji, for example 👍.");
        }

        access.AcquireWriteSlot();
        await gateway.ReactAsync(target, messageId, reaction, ct);
        return new WriteResult("reacted", target.Title, target.Id, messageId);
    }

    public async Task<WriteResult> SendFileAsync(string chat, string fileName, string contentBase64, string? caption, CancellationToken ct)
    {
        access.RequireWrite();
        var target = await ResolveAsync(chat, ct);
        var name = SafeFileName(fileName);
        var text = string.IsNullOrWhiteSpace(caption) ? null : ValidateText(caption, MaxCaptionLength);

        // Base64 is 4/3 the size of the data: reject oversized input before decoding it.
        if (contentBase64 is null || contentBase64.Length > (long)Options.MaxFileBytes * 4 / 3 + 8)
        {
            throw Fail(IntegrationErrorKind.InvalidInput, $"The file is too large (limit {Options.MaxFileBytes} bytes).");
        }

        var content = new byte[contentBase64.Length];
        if (!Convert.TryFromBase64String(contentBase64, content, out var written) || written == 0 || written > Options.MaxFileBytes)
        {
            throw Fail(IntegrationErrorKind.InvalidInput, "contentBase64 must be non-empty, valid base64 within the size limit.");
        }

        access.AcquireWriteSlot();
        var id = await gateway.SendFileAsync(target, name, content.AsSpan(0, written).ToArray(), text, ct);
        return new WriteResult("file sent", target.Title, target.Id, id);
    }

    // ---------- deletes ----------

    public async Task<WriteResult> DeleteMessagesAsync(string chat, IReadOnlyList<int> messageIds, CancellationToken ct)
    {
        access.RequireDelete();
        var target = await ResolveAsync(chat, ct);
        var ids = ValidateIds(messageIds);

        access.AcquireWriteSlot();
        await gateway.DeleteMessagesAsync(target, ids, ct);
        return new WriteResult("deleted", target.Title, target.Id, null);
    }

    // ---------- helpers ----------

    private async Task<ChatRef> ResolveAsync(string? chat, CancellationToken ct)
    {
        var reference = chat?.Trim() ?? string.Empty;
        if (reference.Length is 0 or > 64)
        {
            throw Fail(IntegrationErrorKind.InvalidInput, "A chat is required: \"me\", a numeric chat id, or an @username.");
        }

        var resolved = await gateway.ResolveChatAsync(reference, ct);
        access.Ensure(resolved);
        return resolved;
    }

    /// <summary>Hides card numbers and cuts long text, so neither reaches the model.</summary>
    private MessageInfo CleanMessage(MessageInfo m) => m with { Text = Clean(m.Text) };

    private string? Clean(string? text)
    {
        var redacted = SensitiveDataPolicy.Redact(text);
        return redacted is not null && redacted.Length > Options.MaxMessageChars
            ? string.Concat(redacted.AsSpan(0, Options.MaxMessageChars), "… [truncated]")
            : redacted;
    }

    private static string ValidateText(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maxLength)
        {
            throw Fail(IntegrationErrorKind.InvalidInput, $"The text must not be empty and can be at most {maxLength} characters.");
        }

        if (SensitiveDataPolicy.ContainsCardNumber(text))
        {
            throw Fail(IntegrationErrorKind.InvalidInput, "The text looks like it contains a payment card number. Card data is never sent through this server.");
        }

        return text;
    }

    private static void RequireMessageId(int id)
    {
        if (id <= 0)
        {
            throw Fail(IntegrationErrorKind.InvalidInput, "messageId must be a positive message id.");
        }
    }

    private static List<int> ValidateIds(IReadOnlyList<int>? ids)
    {
        if (ids is null || ids.Count == 0 || ids.Count > MaxMessageIdsPerCall || ids.Any(i => i <= 0))
        {
            throw Fail(IntegrationErrorKind.InvalidInput, $"Give between 1 and {MaxMessageIdsPerCall} positive message ids.");
        }

        return ids.Distinct().ToList();
    }

    private static string SafeFileName(string? fileName)
    {
        var name = Path.GetFileName(fileName?.Trim() ?? string.Empty);
        return name.Length is > 0 and <= 120 && !name.Any(char.IsControl)
            ? name
            : throw Fail(IntegrationErrorKind.InvalidInput, "fileName must be a plain file name of up to 120 characters.");
    }

    private static IntegrationException Fail(IntegrationErrorKind kind, string message) => TelegramAccess.Fail(kind, message);
}
