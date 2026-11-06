using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using McpHub.Core.Errors;

namespace McpHub.Modules.Postman;

internal sealed class PostmanApiClient(HttpClient http) : IPostmanApiClient
{
    public async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (IntegrationException)
        {
            throw; // e.g. missing API key raised by the auth handler: keep its precise message
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure(IntegrationErrorKind.Unavailable, "Postman is currently unreachable. Try again shortly.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await MapErrorAsync(response, cancellationToken);
            }

            try
            {
                var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                return node ?? throw Failure(IntegrationErrorKind.Unknown, "Postman returned an empty response.");
            }
            catch (JsonException ex)
            {
                throw Failure(IntegrationErrorKind.Unknown, "Postman returned a response in an unexpected format.", ex);
            }
        }
    }

    private static async Task<IntegrationException> MapErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        response.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                Failure(IntegrationErrorKind.Unauthorized, "Postman rejected the server's API key (invalid or revoked)."),
            HttpStatusCode.Forbidden =>
                Failure(IntegrationErrorKind.Forbidden, "The server's Postman API key lacks permission for this resource."),
            HttpStatusCode.NotFound =>
                Failure(IntegrationErrorKind.NotFound, "Not found, or the server's API key cannot see it."),
            HttpStatusCode.TooManyRequests =>
                Failure(IntegrationErrorKind.RateLimited, "Postman rate limit reached. Try again later."),
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
                Failure(IntegrationErrorKind.InvalidInput, $"Postman rejected the request: {await ReadErrorAsync(response, cancellationToken)}"),
            >= HttpStatusCode.InternalServerError =>
                Failure(IntegrationErrorKind.Unavailable, "Postman is experiencing problems. Try again shortly."),
            _ =>
                Failure(IntegrationErrorKind.Unknown, $"Postman returned an unexpected status ({(int)response.StatusCode}).")
        };

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var text = node?["error"]?["message"]?.ToString() ?? node?["error"]?["name"]?.ToString() ?? "invalid request";
            var line = text.Split('\n')[0].Trim();
            return line.Length <= 200 ? line : line[..200];
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return "invalid request";
        }
    }

    private static IntegrationException Failure(IntegrationErrorKind kind, string message, Exception? inner = null) =>
        new(kind, PostmanModule.IntegrationName, message, inner);
}
