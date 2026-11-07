using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using McpHub.Core.Errors;
using McpHub.Core.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TL;
using WTelegram;

namespace McpHub.Modules.Telegram;

/// <summary>
/// MTProto implementation on top of WTelegramClient. It uses a session created beforehand by the login tool and never
/// starts a login itself. Telegram errors are translated into messages that are safe to show to the end user.
/// </summary>
internal sealed partial class WTelegramGateway : ITelegramGateway, IDisposable
{
    private const long ChannelOffset = 1_000_000_000_000L;
    private static readonly TimeSpan DialogCacheLifetime = TimeSpan.FromMinutes(2);

    private readonly ICredentialProvider credentials;
    private readonly IConfiguration configuration;
    private readonly IOptions<TelegramOptions> options;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim connectLock = new(1, 1);
    private readonly SemaphoreSlim dialogLock = new(1, 1);
    private readonly ConcurrentDictionary<long, Peered> peers = new();

    private Client? client;
    private User? self;
    private IReadOnlyList<ChatSummary>? dialogs;
    private DateTimeOffset dialogsLoadedAt;

    public WTelegramGateway(
        ICredentialProvider credentials,
        IConfiguration configuration,
        IOptions<TelegramOptions> options,
        TimeProvider clock,
        ILogger<WTelegramGateway> logger)
    {
        this.credentials = credentials;
        this.configuration = configuration;
        this.options = options;
        this.clock = clock;

        // WTelegramClient writes to the console by default. Only warnings and errors go to our log, as one line each.
        WTelegram.Helpers.Log = (level, line) =>
        {
            if (level >= 3)
            {
                logger.LogWarning("Telegram client: {Line}", line);
            }
        };
    }

    private sealed record Peered(ChatRef Chat, InputPeer Peer);

    // ---------- reads ----------

    public Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var c = await GetClientAsync(cancellationToken);
            var me = await GetSelfAsync(c);
            return new AccountInfo(me.id, NameOf(me), me.MainUsername);
        });

    public Task<ChatRef> ResolveChatAsync(string reference, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var c = await GetClientAsync(cancellationToken);
            var key = reference.Trim();

            if (key.Equals("me", StringComparison.OrdinalIgnoreCase) || key.Equals("self", StringComparison.OrdinalIgnoreCase))
            {
                var me = await GetSelfAsync(c);
                var saved = new Peered(new ChatRef(me.id, "user", "Saved Messages", me.MainUsername), InputPeer.Self);
                peers[me.id] = saved;
                return saved.Chat;
            }

            if (long.TryParse(key, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id))
            {
                if (!peers.TryGetValue(id, out var known))
                {
                    await RefreshDialogsAsync(c, force: true);
                    if (!peers.TryGetValue(id, out known))
                    {
                        throw Fail(IntegrationErrorKind.NotFound, "No chat with that id. Use telegram_list_chats to find it.");
                    }
                }

                return known.Chat;
            }

            var username = key.TrimStart('@');
            if (!UsernamePattern().IsMatch(username))
            {
                throw Fail(IntegrationErrorKind.InvalidInput, "The chat must be \"me\", a numeric chat id, or an @username.");
            }

            var resolved = await c.Contacts_ResolveUsername(username);
            var described = Describe(resolved.UserOrChat) ?? throw Fail(IntegrationErrorKind.NotFound, "No chat with that username.");
            peers[described.Chat.Id] = described;
            return described.Chat;
        });

    public Task<IReadOnlyList<ChatSummary>> ListChatsAsync(CancellationToken cancellationToken) =>
        RunAsync(async () => await RefreshDialogsAsync(await GetClientAsync(cancellationToken), force: false));

    public Task<IReadOnlyList<ContactInfo>> ListContactsAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<ContactInfo>>(async () =>
        {
            var c = await GetClientAsync(cancellationToken);
            var contacts = await c.Contacts_GetContacts();
            return contacts.users.Values.Select(u => new ContactInfo(u.id, NameOf(u), u.MainUsername)).ToList();
        });

    public Task<IReadOnlyList<MessageInfo>> GetMessagesAsync(ChatRef chat, int limit, int beforeMessageId, CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<MessageInfo>>(async () =>
        {
            var c = await GetClientAsync(cancellationToken);
            var history = await c.Messages_GetHistory(PeerOf(chat), offset_id: beforeMessageId, limit: limit);
            return history.Messages.OfType<Message>().Select(m => ToInfo(m, history.UserOrChat, chat)).ToList();
        });

    public Task<IReadOnlyList<MessageInfo>> SearchMessagesAsync(ChatRef? chat, string query, int limit, CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<MessageInfo>>(async () =>
        {
            var c = await GetClientAsync(cancellationToken);
            var found = chat is null
                ? await c.Messages_SearchGlobal(query, null, default, default, 0, null, 0, limit)
                : await c.Messages_Search(PeerOf(chat), query, filter: null, limit: limit);
            return found.Messages.OfType<Message>().Select(m => ToInfo(m, found.UserOrChat, chat)).ToList();
        });

    // ---------- writes ----------

    public Task<int> SendMessageAsync(ChatRef chat, string text, int? replyToMessageId, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var c = await GetClientAsync(cancellationToken);
            var sent = await c.SendMessageAsync(PeerOf(chat), text, reply_to_msg_id: replyToMessageId ?? 0);
            return sent.id;
        });

    public Task<int> SendFileAsync(ChatRef chat, string fileName, byte[] content, string? caption, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var c = await GetClientAsync(cancellationToken);
            using var stream = new MemoryStream(content, writable: false);
            var file = await c.UploadFileAsync(stream, fileName);
            var sent = await c.SendMediaAsync(PeerOf(chat), caption ?? string.Empty, file);
            return sent.id;
        });

    public Task EditMessageAsync(ChatRef chat, int messageId, string text, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var c = await GetClientAsync(cancellationToken);
            await c.Messages_EditMessage(PeerOf(chat), messageId, text);
            return true;
        });

    public Task ForwardMessagesAsync(ChatRef from, ChatRef to, IReadOnlyList<int> messageIds, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var c = await GetClientAsync(cancellationToken);
            var ids = messageIds.ToArray();
            await c.Messages_ForwardMessages(PeerOf(from), ids, ids.Select(_ => Random.Shared.NextInt64()).ToArray(), PeerOf(to));
            return true;
        });

    public Task MarkReadAsync(ChatRef chat, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var c = await GetClientAsync(cancellationToken);
            var peer = PeerOf(chat);
            if (peer is InputPeerChannel channel)
            {
                await c.Channels_ReadHistory(new InputChannel(channel.channel_id, channel.access_hash), 0);
            }
            else
            {
                await c.Messages_ReadHistory(peer, 0);
            }

            return true;
        });

    public Task ReactAsync(ChatRef chat, int messageId, string emoji, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var c = await GetClientAsync(cancellationToken);
            await c.Messages_SendReaction(PeerOf(chat), messageId, reaction: [new ReactionEmoji { emoticon = emoji }]);
            return true;
        });

    public Task DeleteMessagesAsync(ChatRef chat, IReadOnlyList<int> messageIds, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var c = await GetClientAsync(cancellationToken);
            await c.DeleteMessages(PeerOf(chat), messageIds.ToArray());
            return true;
        });

    public void Dispose()
    {
        client?.Dispose();
        connectLock.Dispose();
        dialogLock.Dispose();
    }

    // ---------- connection and cache ----------

    private async Task<Client> GetClientAsync(CancellationToken cancellationToken)
    {
        if (client is not null)
        {
            return client;
        }

        await connectLock.WaitAsync(cancellationToken);
        try
        {
            if (client is not null)
            {
                return client;
            }

            var created = await CreateClientAsync(cancellationToken);
            try
            {
                await created.ConnectAsync();
                if (created.UserId == 0)
                {
                    throw Fail(IntegrationErrorKind.Unauthorized,
                        "The Telegram session is not signed in. Run the McpHub.Telegram.Login tool on the server.");
                }

                // Deliberately no LoginUserIfNeeded(): with a valid session UserId is set but User stays null, and that call
                // would then ask for a phone number the server cannot give. A revoked session shows up as a 401 on the first
                // request instead, which is translated into a safe "sign in again" message.
            }
            catch
            {
                created.Dispose();
                throw;
            }

            client = created;
            return created;
        }
        finally
        {
            connectLock.Release();
        }
    }

    private async Task<Client> CreateClientAsync(CancellationToken cancellationToken)
    {
        var apiHash = await credentials.GetAsync(TelegramModule.IntegrationName, cancellationToken);
        var sessionKey = configuration[$"Modules:{TelegramModule.IntegrationName}:Credentials:SessionKey"];
        return TelegramClientFactory.Create(options.Value, apiHash, sessionKey, interactive: null);
    }

    /// <summary>The signed-in account. Fetched once from Telegram because <c>Client.User</c> is empty after resuming a saved session.</summary>
    private async Task<User> GetSelfAsync(Client c)
    {
        if (self is not null)
        {
            return self;
        }

        var users = await c.Users_GetUsers(new InputUserBase[] { new InputUserSelf() });
        self = users.OfType<User>().FirstOrDefault()
               ?? throw Fail(IntegrationErrorKind.Unauthorized, "The Telegram session is no longer valid. Sign in again with the login tool.");
        return self;
    }

    private async Task<IReadOnlyList<ChatSummary>> RefreshDialogsAsync(Client c, bool force)
    {
        await dialogLock.WaitAsync();
        try
        {
            if (!force && dialogs is not null && clock.GetUtcNow() - dialogsLoadedAt < DialogCacheLifetime)
            {
                return dialogs;
            }

            var all = await c.Messages_GetAllDialogs();
            var list = new List<ChatSummary>();
            foreach (var dialog in all.dialogs.OfType<Dialog>())
            {
                var described = Describe(all.UserOrChat(dialog.peer));
                if (described is null)
                {
                    continue;
                }

                peers[described.Chat.Id] = described;
                var last = all.messages.OfType<Message>().FirstOrDefault(m => m.ID == dialog.top_message && PeerId(m.Peer) == described.Chat.Id);
                list.Add(new ChatSummary(
                    described.Chat.Id,
                    described.Chat.Type,
                    described.Chat.Title,
                    described.Chat.Username,
                    dialog.unread_count,
                    last?.message,
                    last is null ? null : ToDate(last.Date)));
            }

            dialogs = list;
            dialogsLoadedAt = clock.GetUtcNow();
            return list;
        }
        finally
        {
            dialogLock.Release();
        }
    }

    private InputPeer PeerOf(ChatRef chat) =>
        peers.TryGetValue(chat.Id, out var known)
            ? known.Peer
            : throw Fail(IntegrationErrorKind.InvalidInput, "Unknown chat. Resolve it first.");

    // ---------- mapping ----------

    private static Peered? Describe(object? entity) => entity switch
    {
        User u => new Peered(
            new ChatRef(u.id, u.flags.HasFlag(User.Flags.bot) ? "bot" : "user", NameOf(u) ?? $"user {u.id}", u.MainUsername),
            u.ToInputPeer()),
        Channel ch => new Peered(
            new ChatRef(-ChannelOffset - ch.id, ch.flags.HasFlag(Channel.Flags.broadcast) ? "channel" : "supergroup", ch.title, ch.MainUsername),
            ch.ToInputPeer()),
        ChatBase c => new Peered(new ChatRef(-c.ID, "group", c.Title, null), c.ToInputPeer()),
        _ => null
    };

    private static string? NameOf(object? entity) => entity switch
    {
        User u => string.Join(' ', new[] { u.first_name, u.last_name }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } name
            ? name
            : u.MainUsername,
        ChatBase c => c.Title,
        _ => null
    };

    private static long PeerId(Peer? peer) => peer switch
    {
        PeerUser u => u.user_id,
        PeerChat c => -c.chat_id,
        PeerChannel ch => -ChannelOffset - ch.channel_id,
        _ => 0
    };

    private static DateTimeOffset ToDate(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    private static MessageInfo ToInfo(Message m, Func<Peer, object?> resolve, ChatRef? chat)
    {
        var outgoing = m.flags.HasFlag(Message.Flags.out_);
        var resolvedChat = chat ?? Describe(resolve(m.Peer))?.Chat;
        string? forwardedFrom = m.fwd_from is null
            ? null
            : m.fwd_from.from_name ?? (m.fwd_from.from_id is { } origin ? NameOf(resolve(origin)) : null);

        return new MessageInfo(
            m.ID,
            resolvedChat?.Id ?? PeerId(m.Peer),
            resolvedChat?.Title ?? string.Empty,
            resolvedChat?.Username,
            ToDate(m.Date),
            outgoing ? "me" : NameOf(resolve(m.From ?? m.Peer)),
            outgoing,
            m.message,
            DescribeMedia(m.media),
            m.reply_to is MessageReplyHeader { reply_to_msg_id: > 0 } reply ? reply.reply_to_msg_id : null,
            forwardedFrom,
            m.edit_date == default ? null : ToDate(m.edit_date));
    }

    /// <summary>Metadata only. File contents are never downloaded, so nothing from a chat is written to the server's disk.</summary>
    private static string? DescribeMedia(MessageMedia? media) => media switch
    {
        null => null,
        MessageMediaPhoto => "photo",
        MessageMediaDocument { document: Document doc } => $"document: {doc.Filename ?? "unnamed"} ({doc.mime_type}, {doc.size} bytes)",
        MessageMediaDocument => "document",
        MessageMediaWebPage => "link preview",
        MessageMediaGeo or MessageMediaGeoLive or MessageMediaVenue => "location",
        MessageMediaContact => "contact card",
        MessageMediaPoll => "poll",
        _ => media.GetType().Name.Replace("MessageMedia", string.Empty, StringComparison.Ordinal).ToLowerInvariant()
    };

    // ---------- errors ----------

    private static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (RpcException ex)
        {
            throw Translate(ex);
        }
        catch (IntegrationException)
        {
            throw;
        }
        catch (WTException ex)
        {
            throw Fail(IntegrationErrorKind.Unavailable, "Telegram is not reachable right now. Try again later.", ex);
        }
    }

    private static IntegrationException Translate(RpcException ex)
    {
        var code = ex.Message ?? string.Empty;
        return ex.Code switch
        {
            420 => Fail(IntegrationErrorKind.RateLimited, "Telegram is rate limiting this account. Try again later.", ex),
            401 => Fail(IntegrationErrorKind.Unauthorized, "The Telegram session is no longer valid. Sign in again with the login tool.", ex),
            403 => Fail(IntegrationErrorKind.Forbidden, "Telegram does not allow this account to do that here.", ex),
            400 when code.Contains("USERNAME", StringComparison.Ordinal) || code.Contains("PEER_ID", StringComparison.Ordinal) =>
                Fail(IntegrationErrorKind.NotFound, "Telegram could not find that chat or user.", ex),
            400 when code.Contains("MESSAGE_ID", StringComparison.Ordinal) || code.Contains("MSG_ID", StringComparison.Ordinal) =>
                Fail(IntegrationErrorKind.NotFound, "Telegram could not find that message.", ex),
            400 => Fail(IntegrationErrorKind.InvalidInput, "Telegram rejected the request.", ex),
            _ => Fail(IntegrationErrorKind.Unknown, "Telegram returned an unexpected error.", ex)
        };
    }

    private static IntegrationException Fail(IntegrationErrorKind kind, string message, Exception? inner = null) =>
        new(kind, TelegramModule.IntegrationName, message, inner);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{3,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
