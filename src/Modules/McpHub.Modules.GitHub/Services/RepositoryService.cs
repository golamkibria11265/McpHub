using System.Text;
using McpHub.Core.Errors;
using McpHub.Modules.GitHub.Access;
using McpHub.Modules.GitHub.Clients;
using McpHub.Modules.GitHub.Models;
using McpHub.Modules.GitHub.Models.Api;
using Microsoft.Extensions.Options;

namespace McpHub.Modules.GitHub.Services;

/// <summary>Use cases: list repositories of an owner, read a text file from a repository.</summary>
public sealed class RepositoryService(
    IGitHubApiClient api,
    RepositoryAccessPolicy access,
    SensitivePathPolicy sensitivePaths,
    IOptions<GitHubOptions> options)
{
    public async Task<IReadOnlyList<RepositorySummary>> ListAsync(string? owner, int? count, CancellationToken cancellationToken)
    {
        var validOwner = access.ResolveOwner(owner);
        var escaped = Uri.EscapeDataString(validOwner);
        var query = $"per_page={options.Value.ClampCount(count)}&sort=pushed";

        IReadOnlyList<ApiRepo> repos;
        try
        {
            repos = await api.GetAsync<List<ApiRepo>>($"orgs/{escaped}/repos?{query}", cancellationToken);
        }
        catch (IntegrationException ex) when (ex.Kind == IntegrationErrorKind.NotFound)
        {
            // The owner is a user account, not an organization.
            repos = await api.GetAsync<List<ApiRepo>>($"users/{escaped}/repos?{query}", cancellationToken);
        }

        return [.. repos.Select(GitHubMapper.ToRepositorySummary)];
    }

    public async Task<FileContent> GetFileAsync(
        string? owner, string? repo, string? path, string? gitRef, CancellationToken cancellationToken)
    {
        var repository = access.Resolve(owner, repo);
        var safePath = sensitivePaths.EnsureReadable(path);
        var validRef = GitHubInput.OptionalRef(gitRef);

        var escapedPath = string.Join('/', safePath.Split('/').Select(Uri.EscapeDataString));
        var url = $"{repository.ApiPath}/contents/{escapedPath}"
                  + (validRef is null ? string.Empty : $"?ref={Uri.EscapeDataString(validRef)}");

        var file = await api.GetAsync<ApiContent>(url, cancellationToken);

        if (file.Type != "file" || file.Encoding != "base64" || string.IsNullOrEmpty(file.Content))
        {
            throw new IntegrationException(
                IntegrationErrorKind.InvalidInput,
                GitHubModule.IntegrationName,
                "The path is not a readable file (it may be empty, too large, or not a regular file).");
        }

        var bytes = Convert.FromBase64String(file.Content.Replace("\n", string.Empty, StringComparison.Ordinal));
        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            throw new IntegrationException(
                IntegrationErrorKind.InvalidInput,
                GitHubModule.IntegrationName,
                "Binary files are not supported.");
        }

        var text = Encoding.UTF8.GetString(bytes);
        var max = options.Value.MaxFileChars;
        var truncated = text.Length > max;

        return new FileContent(file.Path, validRef, file.Size, truncated, truncated ? text[..max] : text);
    }
}
