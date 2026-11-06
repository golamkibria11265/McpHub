using McpHub.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace McpHub.Modules.Postman;

/// <summary>Wires up the Postman integration (read, and optionally write and delete).</summary>
public sealed class PostmanModule : IIntegrationModule
{
    public const string IntegrationName = "Postman";

    public string Name => IntegrationName;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PostmanOptions>()
            .Bind(configuration.GetSection($"Modules:{Name}"))
            .Validate(o => o.AllowedWorkspaceIds.Length > 0 && o.AllowedWorkspaceIds.All(w => w.Length > 0),
                "Modules:Postman:AllowedWorkspaceIds must list at least one Postman workspace id.")
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps,
                "Modules:Postman:BaseUrl must be an absolute https URL.")
            .Validate(o => o.MaxResults > 0 && o.MaxResponseChars > 0 && o.MaxWriteChars > 0,
                "Modules:Postman limits must be positive.")
            .ValidateOnStart();

        services.AddSingleton<PostmanAccess>();
        services.AddSingleton(sp => new SecretPolicy(sp.GetRequiredService<IOptions<PostmanOptions>>().Value.ExtraDeniedKeys));

        services.AddTransient<PostmanAuthHandler>();
        services
            .AddHttpClient<IPostmanApiClient, PostmanApiClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<PostmanOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("McpHub/1.0");
            })
            .AddHttpMessageHandler<PostmanAuthHandler>()
            // Retrying a POST/PUT/DELETE could create duplicates or repeat a delete, so only safe methods are retried.
            .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());

        services.AddTransient<PostmanService>();
    }
}
