namespace McpHub.Modules.Telegram;

/// <summary>
/// Bound from <c>Modules:Telegram</c>. Secrets live under <c>Modules:Telegram:Credentials</c>:
/// <c>Token</c> is the api_hash from my.telegram.org, <c>SessionKey</c> encrypts the session file.
/// </summary>
public sealed class TelegramOptions
{
    /// <summary>The numeric api_id from https://my.telegram.org (API development tools). Not a secret on its own.</summary>
    public int ApiId { get; set; }

    /// <summary>
    /// Where the signed-in session is stored. The file is equivalent to a logged-in Telegram app on your account:
    /// keep it outside the repository, readable only by the service user.
    /// </summary>
    public string SessionPath { get; set; } = "telegram.session";

    /// <summary>When true (default) the server refuses to start unless <c>Credentials:SessionKey</c> is set, so the session file is encrypted at rest.</summary>
    public bool RequireSessionEncryption { get; set; } = true;

    /// <summary>
    /// Optional allow-list of chats (numeric id from telegram_list_chats, or @username). Empty means every chat
    /// except the always-blocked ones. When set, nothing outside the list can be read or written.
    /// </summary>
    public string[] AllowedChats { get; set; } = [];

    /// <summary>Extra chats that are never readable or writable (id or @username), on top of the built-in blocks.</summary>
    public string[] DeniedChats { get; set; } = [];

    /// <summary>Master switch for send, edit, forward, react, mark-read and file tools. Off by default.</summary>
    public bool AllowWrite { get; set; }

    /// <summary>Extra switch for delete tools. Off by default and only effective together with <see cref="AllowWrite"/>.</summary>
    public bool AllowDelete { get; set; }

    /// <summary>Upper bound on list sizes. Keeps responses (and LLM token cost) small.</summary>
    public int MaxResults { get; set; } = 50;

    /// <summary>Message text longer than this is cut when returned to the model.</summary>
    public int MaxMessageChars { get; set; } = 2000;

    /// <summary>Largest file accepted by telegram_send_file.</summary>
    public int MaxFileBytes { get; set; } = 10_000_000;

    /// <summary>Upper bound on write calls per minute, to stay clear of Telegram flood limits and runaway loops.</summary>
    public int MaxWritesPerMinute { get; set; } = 20;

    public int ClampCount(int? requested) => Math.Clamp(requested ?? MaxResults, 1, MaxResults);
}
