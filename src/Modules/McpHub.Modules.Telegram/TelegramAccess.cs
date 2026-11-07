using McpHub.Core.Errors;
using Microsoft.Extensions.Options;

namespace McpHub.Modules.Telegram;

/// <summary>
/// Decides which chats may be touched and whether writes or deletes are switched on. All from configuration,
/// plus two chats that are blocked no matter what.
/// </summary>
public sealed class TelegramAccess(IOptions<TelegramOptions> options, TimeProvider clock)
{
    /// <summary>
    /// 777000 is Telegram's own service chat: it delivers the login codes for this account, so reading it would let
    /// anyone who controls a prompt take the account over. 93372553 is @BotFather, which hands out bot tokens.
    /// </summary>
    private static readonly long[] AlwaysDenied = [777000L, 93372553L];

    private readonly Queue<DateTimeOffset> recentWrites = new();
    private readonly object gate = new();

    private TelegramOptions Options => options.Value;

    public bool IsPermitted(long chatId, string? username)
    {
        if (AlwaysDenied.Contains(chatId))
        {
            return false;
        }

        if (Options.DeniedChats.Any(e => Matches(e, chatId, username)))
        {
            return false;
        }

        return Options.AllowedChats.Length == 0 || Options.AllowedChats.Any(e => Matches(e, chatId, username));
    }

    /// <summary>The message is deliberately the same for blocked and unlisted chats.</summary>
    public void Ensure(ChatRef chat)
    {
        if (!IsPermitted(chat.Id, chat.Username))
        {
            throw Fail(IntegrationErrorKind.Forbidden, "That chat is not available on this server.");
        }
    }

    public void RequireWrite()
    {
        if (!Options.AllowWrite)
        {
            throw Fail(IntegrationErrorKind.Forbidden, "Write operations are disabled on this server.");
        }
    }

    public void RequireDelete()
    {
        RequireWrite();
        if (!Options.AllowDelete)
        {
            throw Fail(IntegrationErrorKind.Forbidden, "Delete operations are disabled on this server.");
        }
    }

    /// <summary>Counts one write against the per-minute budget. Call only after the input has been validated.</summary>
    public void AcquireWriteSlot()
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            while (recentWrites.Count > 0 && now - recentWrites.Peek() >= TimeSpan.FromMinutes(1))
            {
                recentWrites.Dequeue();
            }

            if (recentWrites.Count >= Options.MaxWritesPerMinute)
            {
                throw Fail(IntegrationErrorKind.RateLimited, "Too many Telegram write operations in the last minute. Wait and try again.");
            }

            recentWrites.Enqueue(now);
        }
    }

    internal static bool Matches(string entry, long chatId, string? username)
    {
        var e = entry.Trim();
        if (e.Length == 0)
        {
            return false;
        }

        if (long.TryParse(e, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var id))
        {
            return id == chatId;
        }

        return username is not null && string.Equals(e.TrimStart('@'), username.TrimStart('@'), StringComparison.OrdinalIgnoreCase);
    }

    internal static IntegrationException Fail(IntegrationErrorKind kind, string message) =>
        new(kind, TelegramModule.IntegrationName, message);
}
