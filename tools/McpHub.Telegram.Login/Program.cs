using McpHub.Modules.Telegram;
using Microsoft.Extensions.Configuration;
using TL;

// Sign in to / out of the McpHub Telegram module.
// Telegram sends a login code to the account; type it HERE, on the server. It never goes through a chat with Claude.
//
// Usage:
//   dotnet run --project tools/McpHub.Telegram.Login -- [login] <folder with appsettings.json>
//   dotnet run --project tools/McpHub.Telegram.Login -- logout <folder with appsettings.json>
// Example: dotnet run --project tools/McpHub.Telegram.Login -- login src/McpHub.Host
// Set DOTNET_ENVIRONMENT (Development or Production) to pick appsettings.<Environment>.json. Default: Production.
//
// "logout" ends the session on Telegram's side (the device disappears from Settings > Devices) and deletes the session file.
// Stopping the server does NOT sign out: the session stays valid until you run "logout".

var arguments = args.ToList();
var command = "login";
if (arguments.Count > 0 && arguments[0] is "login" or "logout")
{
    command = arguments[0];
    arguments.RemoveAt(0);
}

var folder = Path.GetFullPath(arguments.Count > 0 ? arguments[0] : Directory.GetCurrentDirectory());
var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

var builder = new ConfigurationBuilder()
    .SetBasePath(folder)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{environment}.json", optional: true);

// Same secret store the host uses in Development (dotnet user-secrets), so both read identical values.
if (environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
{
    builder.AddUserSecrets("mcphub-host-dev");
}

var configuration = builder.AddEnvironmentVariables().Build();

var section = configuration.GetSection("Modules:Telegram");
var options = section.Get<TelegramOptions>() ?? new TelegramOptions();
var apiHash = section["Credentials:Token"];
var sessionKey = section["Credentials:SessionKey"];

if (options.ApiId <= 0 || string.IsNullOrWhiteSpace(apiHash))
{
    Console.Error.WriteLine("Set Modules:Telegram:ApiId and Modules:Telegram:Credentials:Token (api_hash) first. Get both at https://my.telegram.org.");
    return 1;
}

if (options.RequireSessionEncryption && string.IsNullOrWhiteSpace(sessionKey))
{
    Console.Error.WriteLine("Set Modules:Telegram:Credentials:SessionKey (a long random value) so the session file is encrypted.");
    return 1;
}

options.SessionPath = Path.GetFullPath(options.SessionPath, folder);

return command == "logout"
    ? await LogOutAsync(options, apiHash, sessionKey)
    : await LogInAsync(options, apiHash, sessionKey);

static async Task<int> LogInAsync(TelegramOptions options, string apiHash, string? sessionKey)
{
    using var client = TelegramClientFactory.Create(options, apiHash, sessionKey, Ask);

    if (File.Exists(options.SessionPath))
    {
        // A session file may already exist. If it still works there is nothing to do; if not, sign in again below.
        try
        {
            await client.ConnectAsync();
            if (client.UserId != 0)
            {
                var users = await client.Users_GetUsers(new InputUserBase[] { new InputUserSelf() });
                if (users.OfType<User>().FirstOrDefault() is { } existing)
                {
                    Console.WriteLine($"Already signed in as {Describe(existing)}. Run \"logout\" first to switch accounts.");
                    return 0;
                }
            }
        }
        catch (Exception ex) when (ex is WTelegram.WTException or IOException)
        {
            Console.WriteLine("The existing session is no longer valid. Signing in again.");
        }
    }

    var me = await client.LoginUserIfNeeded();

    if (!OperatingSystem.IsWindows() && File.Exists(options.SessionPath))
    {
        File.SetUnixFileMode(options.SessionPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    Console.WriteLine($"Signed in as {Describe(me)}.");
    Console.WriteLine($"Session saved to {options.SessionPath}. Keep it private: it is a full login to this account.");
    return 0;
}

static async Task<int> LogOutAsync(TelegramOptions options, string apiHash, string? sessionKey)
{
    if (!File.Exists(options.SessionPath))
    {
        Console.WriteLine("No session file found. Nothing to log out.");
        return 0;
    }

    var loggedOut = false;
    try
    {
        using var client = TelegramClientFactory.Create(options, apiHash, sessionKey);
        await client.ConnectAsync();
        if (client.UserId != 0)
        {
            await client.Auth_LogOut();
            loggedOut = true;
        }
    }
    catch (Exception ex) when (ex is WTelegram.WTException or IOException)
    {
        Console.Error.WriteLine("Could not reach Telegram to log out. The local session file will still be deleted; " +
                                "end the device under Telegram > Settings > Devices.");
    }

    File.Delete(options.SessionPath);
    Console.WriteLine(loggedOut
        ? "Logged out on Telegram and deleted the session file."
        : "Deleted the session file.");
    return 0;
}

static string Describe(User user) =>
    $"{user.first_name} {user.last_name}".Trim() + (user.MainUsername is null ? string.Empty : $" (@{user.MainUsername})");

static string? Ask(string what) => what switch
{
    "phone_number" => Prompt("Phone number in international format (for example +8801XXXXXXXXX): "),
    "verification_code" => Prompt("Login code Telegram sent to your account: "),
    "password" => PromptSecret("Two-step verification password: "),
    _ => null
};

static string Prompt(string label)
{
    Console.Write(label);
    return Console.ReadLine()?.Trim() ?? string.Empty;
}

static string PromptSecret(string label)
{
    Console.Write(label);
    var text = new System.Text.StringBuilder();
    while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace && text.Length > 0)
        {
            text.Length--;
        }
        else if (!char.IsControl(key.KeyChar))
        {
            text.Append(key.KeyChar);
        }
    }

    Console.WriteLine();
    return text.ToString();
}
