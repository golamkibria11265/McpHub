using System.Globalization;
using WTelegram;

namespace McpHub.Modules.Telegram;

/// <summary>Builds the MTProto client. Shared by the server and the one-time login tool so both use the same session file.</summary>
public static class TelegramClientFactory
{
    /// <param name="interactive">
    /// Answers Telegram's login questions ("phone_number", "verification_code", "password"). The server passes null:
    /// it never logs in by itself, only the login tool does, so codes and passwords never travel through a chat.
    /// </param>
    public static Client Create(TelegramOptions options, string apiHash, string? sessionKey, Func<string, string?>? interactive = null)
    {
        string? Config(string what) => what switch
        {
            "api_id" => options.ApiId.ToString(CultureInfo.InvariantCulture),
            "api_hash" => apiHash,
            "session_pathname" => options.SessionPath,
            "session_key" => string.IsNullOrWhiteSpace(sessionKey) ? null : sessionKey,
            _ => interactive?.Invoke(what)
        };

        return new Client(Config);
    }
}
