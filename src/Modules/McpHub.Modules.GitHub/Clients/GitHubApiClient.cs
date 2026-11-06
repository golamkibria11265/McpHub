using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using McpHub.Core.Errors;

namespace McpHub.Modules.GitHub.Clients;

internal sealed class GitHubApiClient(HttpClient http) : IGitHubApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<T> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(relativePath, cancellationToken);

        try
        {
            var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return result ?? throw Failure(IntegrationErrorKind.Unknown, "GitHub returned an empty response.");
        }
        catch (JsonException ex)
        {
            throw Failure(IntegrationErrorKind.Unknown, "GitHub returned a response in an unexpected format.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string relativePath, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await http.GetAsync(relativePath, cancellationToken);
        }
        catch (IntegrationException)
        {
            throw; // e.g. missing credential raised by the auth handler: keep its precise message
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Network failure, timeout, or open circuit breaker.
            throw Failure(IntegrationErrorKind.Unavailable, "GitHub is currently unreachable. Try again shortly.", ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var error = MapError(response);
        response.Dispose();
        throw error;
    }

    private static IntegrationException MapError(HttpResponseMessage response)
    {
        var rateLimited = response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
                          && remaining.FirstOrDefault() == "0";

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                Failure(IntegrationErrorKind.Unauthorized, "GitHub rejected the server's credential (expired or revoked)."),
            HttpStatusCode.Forbidden when rateLimited =>
                Failure(IntegrationErrorKind.RateLimited, "GitHub rate limit reached. Try again later."),
            HttpStatusCode.Forbidden =>
                Failure(IntegrationErrorKind.Forbidden, "The server's GitHub credential lacks permission for this resource."),
            HttpStatusCode.NotFound =>
                Failure(IntegrationErrorKind.NotFound, "Not found, or the server's credential cannot see it."),
            HttpStatusCode.TooManyRequests =>
                Failure(IntegrationErrorKind.RateLimited, "GitHub rate limit reached. Try again later."),
            HttpStatusCode.UnprocessableEntity =>
                Failure(IntegrationErrorKind.InvalidInput, "GitHub could not process the request (check the query)."),
            >= HttpStatusCode.InternalServerError =>
                Failure(IntegrationErrorKind.Unavailable, "GitHub is experiencing problems. Try again shortly."),
            _ =>
                Failure(IntegrationErrorKind.Unknown, $"GitHub returned an unexpected status ({(int)response.StatusCode}).")
        };
    }

    private static IntegrationException Failure(IntegrationErrorKind kind, string message, Exception? inner = null) =>
        new(kind, GitHubModule.IntegrationName, message, inner);
}
