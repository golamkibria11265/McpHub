using McpHub.Modules.Telegram;
using Microsoft.Extensions.Configuration;

// One-time sign-in for the McpHub Telegram module.
// Telegram sends a login code to the account; type it HERE, on the server. It never goes through a chat with Claude.
//
// Usage:  dotnet run --project tools/McpHub.Telegram.Login -- <folder with appsettings.json>
// Example: dotnet run --project tools/McpHub.Telegram.Login -- src/McpHub.Host
// Set DOTNET_ENVIRONMENT (Development or Production) to pick appsettings.<Environment>.json. Default: Production.

var folder = Path.GetFullPath(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());
var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

var configuration = new ConfigurationBuilder()
    .SetBasePath(folder)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{environment}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

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

using var client = TelegramClientFactory.Create(options, apiHash, sessionKey, Ask);
var me = await client.LoginUserIfNeeded();

if (!OperatingSystem.IsWindows() && File.Exists(options.SessionPath))
{
    File.SetUnixFileMode(options.SessionPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
}

Console.WriteLine($"Signed in as {me.first_name} {me.last_name}".TrimEnd() + (me.MainUsername is null ? string.Empty : $" (@{me.MainUsername})"));
Console.WriteLine($"Session saved to {options.SessionPath}. Keep it private: it is a full login to this account.");
return 0;

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
