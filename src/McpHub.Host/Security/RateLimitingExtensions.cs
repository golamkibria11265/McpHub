using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace McpHub.Host.Security;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int PermitLimit { get; set; } = 60;

    public int WindowSeconds { get; set; } = 60;
}

internal static class RateLimitingExtensions
{
    public const string PolicyName = "mcp";

    /// <summary>Fixed-window limit per authenticated user (falls back to client IP). Protects both this server and upstream APIs.</summary>
    public static IServiceCollection AddMcpHubRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PolicyName, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.User.Identity?.Name
                                  ?? context.Connection.RemoteIpAddress?.ToString()
                                  ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit,
                        Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                        QueueLimit = 0
                    }));
        });

        return services;
    }
}
