using Microsoft.Extensions.DependencyInjection;

namespace McpHub.Infrastructure.Http;

public static class IntegrationHttpClientExtensions
{
    /// <summary>
    /// Registers a typed HttpClient with the standard resilience pipeline (rate limiter, total timeout, retry with
    /// backoff, circuit breaker, attempt timeout). Every integration gets identical failure handling by using this.
    /// Add an auth <c>DelegatingHandler</c> on the returned builder; it runs inside the resilience pipeline.
    /// </summary>
    public static IHttpClientBuilder AddIntegrationHttpClient<TClient, TImplementation>(
        this IServiceCollection services,
        Action<IServiceProvider, HttpClient> configure)
        where TClient : class
        where TImplementation : class, TClient
    {
        var builder = services.AddHttpClient<TClient, TImplementation>(configure);
        builder.AddStandardResilienceHandler();
        return builder;
    }
}
