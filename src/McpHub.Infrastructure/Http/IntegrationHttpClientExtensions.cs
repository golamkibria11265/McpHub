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
        // Retrying a POST/PUT/PATCH/DELETE could create duplicates or repeat a delete, so only GET/HEAD/OPTIONS are retried (on 5xx, 408 and 429).
        builder.AddStandardResilienceHandler(o =>
        {
            o.Retry.ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Result is { RequestMessage.Method: var method } response
                && (method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options)
                && ((int)response.StatusCode >= 500 || (int)response.StatusCode is 408 or 429));
        });
        return builder;
    }
}
