using McpHub.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace McpHub.Modules.Telegram;

/// <summary>Wires up the Telegram integration: your own account through the user API (MTProto), read, and optionally write and delete.</summary>
public sealed class TelegramModule : IIntegrationModule
{
    public const string IntegrationName = "Telegram";

    public string Name => IntegrationName;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection($"Modules:{Name}"))
            .Validate(o => o.ApiId > 0, "Modules:Telegram:ApiId must be the numeric api_id from my.telegram.org.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.SessionPath), "Modules:Telegram:SessionPath is required.")
            .Validate(o => !o.RequireSessionEncryption || !string.IsNullOrWhiteSpace(configuration[$"Modules:{Name}:Credentials:SessionKey"]),
                "Modules:Telegram:Credentials:SessionKey is required (or set RequireSessionEncryption to false, not recommended): the session file is a full login to the account.")
            .Validate(o => o.MaxResults > 0 && o.MaxMessageChars > 0 && o.MaxFileBytes > 0 && o.MaxWritesPerMinute > 0,
                "Modules:Telegram limits must be positive.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<TelegramAccess>();
        services.AddSingleton<ITelegramGateway, WTelegramGateway>();
        services.AddTransient<TelegramService>();
    }
}
