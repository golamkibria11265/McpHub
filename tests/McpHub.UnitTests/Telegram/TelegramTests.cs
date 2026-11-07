using McpHub.Core.Errors;
using McpHub.Modules.Telegram;
using Microsoft.Extensions.Options;

namespace McpHub.UnitTests.Telegram;

internal sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeTelegramGateway : ITelegramGateway
{
    public Dictionary<string, ChatRef> Chats { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alice"] = new(1001, "user", "Alice", "alice_ssl"),
        ["team"] = new(-1002003004005, "supergroup", "Team", null),
        ["777000"] = new(777000, "user", "Telegram", null),
        ["93372553"] = new(93372553, "bot", "BotFather", "BotFather")
    };

    public List<string> Calls { get; } = [];

    public List<ChatSummary> Dialogs { get; } = [];

    public List<MessageInfo> Messages { get; } = [];

    public Task<AccountInfo> GetAccountAsync(CancellationToken ct) => Task.FromResult(new AccountInfo(1, "Me", "me_user"));

    public Task<ChatRef> ResolveChatAsync(string reference, CancellationToken ct) =>
        Chats.TryGetValue(reference.TrimStart('@'), out var chat)
            ? Task.FromResult(chat)
            : Task.FromException<ChatRef>(new IntegrationException(IntegrationErrorKind.NotFound, "Telegram", "No chat."));

    public Task<IReadOnlyList<ChatSummary>> ListChatsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ChatSummary>>(Dialogs);

    public Task<IReadOnlyList<ContactInfo>> ListContactsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContactInfo>>([new ContactInfo(1001, "Alice", "alice_ssl"), new ContactInfo(777000, "Telegram", null)]);

    public Task<IReadOnlyList<MessageInfo>> GetMessagesAsync(ChatRef chat, int limit, int beforeMessageId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MessageInfo>>(Messages.Take(limit).ToList());

    public Task<IReadOnlyList<MessageInfo>> SearchMessagesAsync(ChatRef? chat, string query, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MessageInfo>>(Messages.Take(limit).ToList());

    public Task<int> SendMessageAsync(ChatRef chat, string text, int? replyToMessageId, CancellationToken ct)
    {
        Calls.Add($"send:{chat.Id}:{text}");
        return Task.FromResult(42);
    }

    public Task<int> SendFileAsync(ChatRef chat, string fileName, byte[] content, string? caption, CancellationToken ct)
    {
        Calls.Add($"file:{chat.Id}:{fileName}:{content.Length}");
        return Task.FromResult(43);
    }

    public Task EditMessageAsync(ChatRef chat, int messageId, string text, CancellationToken ct)
    {
        Calls.Add($"edit:{chat.Id}:{messageId}");
        return Task.CompletedTask;
    }

    public Task ForwardMessagesAsync(ChatRef from, ChatRef to, IReadOnlyList<int> messageIds, CancellationToken ct)
    {
        Calls.Add($"forward:{from.Id}->{to.Id}");
        return Task.CompletedTask;
    }

    public Task MarkReadAsync(ChatRef chat, CancellationToken ct)
    {
        Calls.Add($"read:{chat.Id}");
        return Task.CompletedTask;
    }

    public Task ReactAsync(ChatRef chat, int messageId, string emoji, CancellationToken ct)
    {
        Calls.Add($"react:{chat.Id}:{messageId}");
        return Task.CompletedTask;
    }

    public Task DeleteMessagesAsync(ChatRef chat, IReadOnlyList<int> messageIds, CancellationToken ct)
    {
        Calls.Add($"delete:{chat.Id}:{string.Join(',', messageIds)}");
        return Task.CompletedTask;
    }
}

public sealed class SensitiveDataPolicyTests
{
    // 4111 1111 1111 1111 is the well-known public test number (passes Luhn). It is not a real card.
    [Theory]
    [InlineData("pay with 4111111111111111 now")]
    [InlineData("card 4111 1111 1111 1111.")]
    [InlineData("4111-1111-1111-1111")]
    public void Card_numbers_are_detected_and_masked(string text)
    {
        Assert.True(SensitiveDataPolicy.ContainsCardNumber(text));
        var redacted = SensitiveDataPolicy.Redact(text)!;
        Assert.Contains(SensitiveDataPolicy.Mask, redacted);
        Assert.DoesNotContain("1111", redacted);
    }

    [Theory]
    [InlineData("order 1234567890123456 shipped")] // 16 digits but fails Luhn
    [InlineData("call +8801712345678")]
    [InlineData("timestamp 1730980000000")]
    [InlineData("41111111111111111111111")] // longer than any card number
    [InlineData("hello world")]
    public void Ordinary_numbers_are_left_alone(string text)
    {
        Assert.False(SensitiveDataPolicy.ContainsCardNumber(text));
        Assert.Equal(text, SensitiveDataPolicy.Redact(text));
    }

    [Fact]
    public void Null_and_empty_pass_through()
    {
        Assert.Null(SensitiveDataPolicy.Redact(null));
        Assert.Equal(string.Empty, SensitiveDataPolicy.Redact(string.Empty));
    }
}

public sealed class TelegramAccessTests
{
    private static TelegramAccess Access(TelegramOptions? options = null, FakeClock? clock = null) =>
        new(Options.Create(options ?? new TelegramOptions()), clock ?? new FakeClock());

    [Theory]
    [InlineData(777000L)] // Telegram service chat: delivers login codes
    [InlineData(93372553L)] // BotFather: hands out bot tokens
    public void Account_takeover_chats_are_always_blocked(long id)
    {
        Assert.False(Access().IsPermitted(id, null));
        Assert.False(Access(new TelegramOptions { AllowedChats = [id.ToString()] }).IsPermitted(id, null));
    }

    [Fact]
    public void Everything_else_is_allowed_when_no_list_is_configured()
    {
        Assert.True(Access().IsPermitted(1001, "alice_ssl"));
    }

    [Fact]
    public void Denied_chats_match_by_id_or_username_ignoring_case_and_at_sign()
    {
        var access = Access(new TelegramOptions { DeniedChats = ["@Finance_Bot", "555"] });

        Assert.False(access.IsPermitted(1, "finance_bot"));
        Assert.False(access.IsPermitted(555, null));
        Assert.True(access.IsPermitted(556, "other"));
    }

    [Fact]
    public void Allow_list_restricts_everything_outside_it()
    {
        var access = Access(new TelegramOptions { AllowedChats = ["@alice_ssl", "-1002003004005"] });

        Assert.True(access.IsPermitted(1001, "Alice_SSL"));
        Assert.True(access.IsPermitted(-1002003004005, null));
        Assert.False(access.IsPermitted(2002, "bob"));
    }

    [Fact]
    public void Writes_and_deletes_are_off_by_default_and_delete_needs_write()
    {
        Assert.Equal(IntegrationErrorKind.Forbidden, Assert.Throws<IntegrationException>(() => Access().RequireWrite()).Kind);
        Assert.Throws<IntegrationException>(() => Access(new TelegramOptions { AllowDelete = true }).RequireDelete());
        Assert.Throws<IntegrationException>(() => Access(new TelegramOptions { AllowWrite = true }).RequireDelete());
        Access(new TelegramOptions { AllowWrite = true, AllowDelete = true }).RequireDelete();
    }

    [Fact]
    public void Write_budget_is_enforced_per_minute()
    {
        var clock = new FakeClock();
        var access = Access(new TelegramOptions { MaxWritesPerMinute = 2 }, clock);

        access.AcquireWriteSlot();
        access.AcquireWriteSlot();
        Assert.Equal(IntegrationErrorKind.RateLimited, Assert.Throws<IntegrationException>(access.AcquireWriteSlot).Kind);

        clock.Now = clock.Now.AddSeconds(61);
        access.AcquireWriteSlot();
    }
}

public sealed class TelegramServiceTests
{
    private readonly FakeTelegramGateway gateway = new();

    private TelegramService Service(TelegramOptions? options = null)
    {
        var wrapped = Options.Create(options ?? new TelegramOptions());
        return new TelegramService(gateway, new TelegramAccess(wrapped, new FakeClock()), wrapped);
    }

    private static TelegramOptions Writable() => new() { AllowWrite = true, AllowDelete = true };

    private static MessageInfo Message(int id, string text) =>
        new(id, 1001, "Alice", "alice_ssl", DateTimeOffset.UnixEpoch, "Alice", false, text, null, null, null, null);

    [Fact]
    public async Task Sending_is_refused_while_writes_are_disabled()
    {
        var ex = await Assert.ThrowsAsync<IntegrationException>(() => Service().SendMessageAsync("alice", "hi", null, default));

        Assert.Equal(IntegrationErrorKind.Forbidden, ex.Kind);
        Assert.Empty(gateway.Calls);
    }

    [Fact]
    public async Task Sending_works_when_enabled()
    {
        var result = await Service(Writable()).SendMessageAsync("@alice", "hi", null, default);

        Assert.Equal(42, result.MessageId);
        Assert.Equal(new[] { "send:1001:hi" }, gateway.Calls);
    }

    [Theory]
    [InlineData("777000")]
    [InlineData("93372553")]
    public async Task Blocked_chats_cannot_be_read_or_written(string chat)
    {
        var service = Service(Writable());

        await Assert.ThrowsAsync<IntegrationException>(() => service.GetMessagesAsync(chat, 10, null, default));
        await Assert.ThrowsAsync<IntegrationException>(() => service.SendMessageAsync(chat, "hello", null, default));
        await Assert.ThrowsAsync<IntegrationException>(() => service.ForwardMessagesAsync("alice", chat, [1], default));
        await Assert.ThrowsAsync<IntegrationException>(() => service.ForwardMessagesAsync(chat, "alice", [1], default));
        Assert.Empty(gateway.Calls);
    }

    [Fact]
    public async Task Chats_outside_the_allow_list_are_forbidden()
    {
        var options = Writable();
        options.AllowedChats = ["@alice_ssl"];

        var ex = await Assert.ThrowsAsync<IntegrationException>(() => Service(options).SendMessageAsync("team", "hi", null, default));

        Assert.Equal(IntegrationErrorKind.Forbidden, ex.Kind);
        Assert.Empty(gateway.Calls);
    }

    [Fact]
    public async Task Outgoing_text_with_a_card_number_is_refused()
    {
        var ex = await Assert.ThrowsAsync<IntegrationException>(() =>
            Service(Writable()).SendMessageAsync("alice", "my card is 4111 1111 1111 1111", null, default));

        Assert.Equal(IntegrationErrorKind.InvalidInput, ex.Kind);
        Assert.Empty(gateway.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_text_is_refused_and_does_not_use_the_write_budget(string text)
    {
        var service = Service(new TelegramOptions { AllowWrite = true, MaxWritesPerMinute = 1 });

        await Assert.ThrowsAsync<IntegrationException>(() => service.SendMessageAsync("alice", text, null, default));
        await service.SendMessageAsync("alice", "ok", null, default);
    }

    [Fact]
    public async Task Too_long_text_is_refused()
    {
        await Assert.ThrowsAsync<IntegrationException>(() =>
            Service(Writable()).SendMessageAsync("alice", new string('a', 4097), null, default));
    }

    [Fact]
    public async Task Deleting_needs_the_delete_switch_and_valid_ids()
    {
        var service = Service(new TelegramOptions { AllowWrite = true });
        await Assert.ThrowsAsync<IntegrationException>(() => service.DeleteMessagesAsync("alice", [1], default));

        var full = Service(Writable());
        await Assert.ThrowsAsync<IntegrationException>(() => full.DeleteMessagesAsync("alice", [], default));
        await Assert.ThrowsAsync<IntegrationException>(() => full.DeleteMessagesAsync("alice", [0], default));

        await full.DeleteMessagesAsync("alice", [5, 5, 6], default);
        Assert.Equal(new[] { "delete:1001:5,6" }, gateway.Calls);
    }

    [Fact]
    public async Task Messages_returned_to_the_model_have_card_numbers_masked_and_long_text_cut()
    {
        gateway.Messages.Add(Message(1, "card 4111111111111111 thanks"));
        gateway.Messages.Add(Message(2, new string('x', 50)));

        var list = await Service(new TelegramOptions { MaxMessageChars = 10 }).GetMessagesAsync("alice", 10, null, default);

        Assert.DoesNotContain("4111", list.Messages[0].Text);
        Assert.EndsWith("[truncated]", list.Messages[1].Text);
    }

    [Fact]
    public async Task Paging_reports_whether_more_messages_exist()
    {
        for (var i = 1; i <= 3; i++)
        {
            gateway.Messages.Add(Message(i, "m"));
        }

        var list = await Service().GetMessagesAsync("alice", 2, null, default);

        Assert.Equal(2, list.Messages.Count);
        Assert.True(list.HasMore);
    }

    [Fact]
    public async Task Global_search_drops_hits_from_blocked_chats()
    {
        gateway.Messages.Add(Message(1, "your login code is [PLACEHOLDER]") with { ChatId = 777000, ChatTitle = "Telegram", ChatUsername = null });
        gateway.Messages.Add(Message(2, "lunch?"));

        var result = await Service().SearchMessagesAsync("code", null, 10, default);

        Assert.Single(result.Messages);
        Assert.Equal(2, result.Messages[0].Id);
    }

    [Fact]
    public async Task Chat_and_contact_lists_hide_blocked_chats()
    {
        gateway.Dialogs.Add(new ChatSummary(777000, "user", "Telegram", null, 1, "code", null));
        gateway.Dialogs.Add(new ChatSummary(1001, "user", "Alice", "alice_ssl", 0, "hi", null));

        var chats = await Service().ListChatsAsync(null, null, null, default);
        var contacts = await Service().ListContactsAsync(null, null, default);

        Assert.Equal(new[] { 1001L }, chats.Chats.Select(c => c.Id));
        Assert.Equal(new[] { 1001L }, contacts.Contacts.Select(c => c.Id));
    }

    [Fact]
    public async Task Chat_list_filters_by_text_and_type()
    {
        gateway.Dialogs.Add(new ChatSummary(1001, "user", "Alice", "alice_ssl", 0, null, null));
        gateway.Dialogs.Add(new ChatSummary(-1002003004005, "supergroup", "Team Alpha", null, 0, null, null));

        Assert.Single((await Service().ListChatsAsync("alpha", null, null, default)).Chats);
        Assert.Single((await Service().ListChatsAsync(null, "USER", null, default)).Chats);
    }

    [Fact]
    public async Task Files_must_be_valid_base64_within_the_size_limit_with_a_plain_name()
    {
        var service = Service(new TelegramOptions { AllowWrite = true, MaxFileBytes = 8 });
        var ok = Convert.ToBase64String("12345678"u8.ToArray());
        var tooBig = Convert.ToBase64String("123456789012"u8.ToArray());

        await service.SendFileAsync("alice", "a.txt", ok, null, default);
        Assert.Equal(new[] { "file:1001:a.txt:8" }, gateway.Calls);

        await Assert.ThrowsAsync<IntegrationException>(() => service.SendFileAsync("alice", "a.txt", tooBig, null, default));
        await Assert.ThrowsAsync<IntegrationException>(() => service.SendFileAsync("alice", "a.txt", "not base64!!", null, default));
        await Assert.ThrowsAsync<IntegrationException>(() => service.SendFileAsync("alice", "", ok, null, default));
    }

    [Fact]
    public async Task File_names_are_reduced_to_their_last_segment()
    {
        await Service(Writable()).SendFileAsync("alice", "../../etc/passwd", Convert.ToBase64String(new byte[] { 1, 2, 3 }), null, default);

        Assert.Equal(new[] { "file:1001:passwd:3" }, gateway.Calls);
    }
}
