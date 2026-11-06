using System.Text;
using System.Text.Json.Nodes;
using McpHub.Core.Errors;
using McpHub.Modules.GitHub.Access;
using McpHub.Modules.GitHub.Clients;
using McpHub.Modules.GitHub.Models;
using McpHub.Modules.GitHub.Models.Api;
using Microsoft.Extensions.Options;

namespace McpHub.Modules.GitHub.Services;

/// <summary>
/// Write use cases on the one configured owner: issues, comments, branches, file commits, pull requests, merges, deletes.
/// Every method first checks the switches in configuration, then the input, then the content for secrets.
/// </summary>
public sealed class GitHubWriteService(
    IGitHubWriteClient writer,
    IGitHubApiClient reader,
    RepositoryAccessPolicy access,
    SensitivePathPolicy sensitivePaths,
    IOptions<GitHubOptions> options)
{
    private GitHubOptions Options => options.Value;

    public async Task<WriteOutcome> CreateIssueAsync(string? repo, string? title, string? body, IReadOnlyList<string>? labels, CancellationToken ct)
    {
        RequireWrite();
        var repository = access.Resolve(access.ConfiguredOwner, repo);
        var payload = new JsonObject { ["title"] = Text(title, "title", 256, required: true), ["body"] = Text(body, "body", 20_000) };
        if (Labels(labels) is { Count: > 0 } validLabels)
        {
            payload["labels"] = new JsonArray([.. validLabels.Select(l => (JsonNode?)JsonValue.Create(l))]);
        }

        var result = await writer.SendAsync(HttpMethod.Post, $"{repository.ApiPath}/issues", payload, ct);
        return Outcome("issue created", repository, result, number: true);
    }

    public async Task<WriteOutcome> UpdateIssueAsync(
        string? repo, int number, string? title, string? body, string? state, IReadOnlyList<string>? labels, CancellationToken ct)
    {
        RequireWrite();
        var repository = access.Resolve(access.ConfiguredOwner, repo);
        var payload = new JsonObject();
        if (!string.IsNullOrWhiteSpace(title))
        {
            payload["title"] = Text(title, "title", 256, required: true);
        }

        if (body is not null)
        {
            payload["body"] = Text(body, "body", 20_000);
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            payload["state"] = state.Trim().ToLowerInvariant() is "open" or "closed"
                ? state.Trim().ToLowerInvariant()
                : throw Invalid("'state' must be 'open' or 'closed'.");
        }

        if (labels is not null)
        {
            payload["labels"] = new JsonArray([.. Labels(labels).Select(l => (JsonNode?)JsonValue.Create(l))]);
        }

        if (payload.Count == 0)
        {
            throw Invalid("Nothing to change: pass a title, body, state or labels.");
        }

        var result = await writer.SendAsync(HttpMethod.Patch, $"{repository.ApiPath}/issues/{Number(number)}", payload, ct);
        return Outcome("issue updated", repository, result, number: true);
    }

    public async Task<WriteOutcome> AddCommentAsync(string? repo, int number, string? body, CancellationToken ct)
    {
        RequireWrite();
        var repository = access.Resolve(access.ConfiguredOwner, repo);
        var payload = new JsonObject { ["body"] = Text(body, "body", 20_000, required: true) };
        var result = await writer.SendAsync(HttpMethod.Post, $"{repository.ApiPath}/issues/{Number(number)}/comments", payload, ct);
        return new WriteOutcome("comment added", repository.ToString(), number, null, Str(result["html_url"]));
    }

    public async Task<WriteOutcome> CreateBranchAsync(string? repo, string? branch, string? fromRef, CancellationToken ct)
    {
        RequireWrite();
        var repository = access.Resolve(access.ConfiguredOwner, repo);
        var name = GitHubInput.RequireRef(branch, "branch");
        var source = GitHubInput.OptionalRef(fromRef, "fromRef") ?? await DefaultBranchAsync(repository, ct);

        var sha = Str((await writer.SendAsync(HttpMethod.Get, $"{repository.ApiPath}/git/ref/heads/{EscapePath(source)}", null, ct))["object"]?["sha"])
                  ?? throw new IntegrationException(IntegrationErrorKind.NotFound, GitHubModule.IntegrationName, $"Branch '{source}' was not found.");

        var payload = new JsonObject { ["ref"] = $"refs/heads/{name}", ["sha"] = sha };
        await writer.SendAsync(HttpMethod.Post, $"{repository.ApiPath}/git/refs", payload, ct);
        return new WriteOutcome("branch created", repository.ToString(), null, name, null);
    }

    public async Task<WriteOutcome> PutFileAsync(
        string? repo, string? path, string? content, string? message, string? branch, CancellationToken ct)
    {
        RequireWrite();
        var repository = access.Resolve(access.ConfiguredOwner, repo);
        var safePath = sensitivePaths.EnsureReadable(path);
        var branchName = GitHubInput.RequireRef(branch, "branch");
        await EnsureNotProtectedAsync(repository, branchName, "commit to", ct);

        if (content is null || content.Length > Options.MaxWriteChars)
        {
            throw Invalid($"'content' is required and must be at most {Options.MaxWriteChars} characters.");
        }

        SecretScanner.EnsureClean(content, "content");
        var commitMessage = Text(message, "message", 500, required: true);
        var url = $"{repository.ApiPath}/contents/{EscapeFilePath(safePath)}";

        // Updating an existing file needs its current blob sha; a missing file means create.
        string? sha = null;
        try
        {
            var existing = await writer.SendAsync(HttpMethod.Get, $"{url}?ref={Uri.EscapeDataString(branchName)}", null, ct);
            if (existing is not JsonObject || Str(existing["type"]) != "file")
            {
                throw Invalid("The path is not a file.");
            }

            sha = Str(existing["sha"]);
        }
        catch (IntegrationException ex) when (ex.Kind == IntegrationErrorKind.NotFound)
        {
            // New file.
        }

        var payload = new JsonObject
        {
            ["message"] = commitMessage,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
            ["branch"] = branchName
        };
        if (sha is not null)
        {
            payload["sha"] = sha;
        }

        var result = await writer.SendAsync(HttpMethod.Put, url, payload, ct);
        return new WriteOutcome(sha is null ? "file created" : "file updated", repository.ToString(), null, $"{branchName}:{safePath}",
            Str(result["commit"]?["html_url"]));
    }

    public async Task<WriteOutcome> CreatePullRequestAsync(
        string? repo, string? title, string? head, string? baseBranch, string? body, bool draft, CancellationToken ct)
    {
        RequireWrite();
        var repository = access.Resolve(access.ConfiguredOwner, repo);
        var payload = new JsonObject
        {
            ["title"] = Text(title, "title", 256, required: true),
            ["head"] = GitHubInput.RequireRef(head, "head"),
            ["base"] = GitHubInput.RequireRef(baseBranch, "base"),
            ["body"] = Text(body, "body", 20_000),
            ["draft"] = draft
        };

        var result = await writer.SendAsync(HttpMethod.Post, $"{repository.ApiPath}/pulls", payload, ct);
        return Outcome("pull request created", repository, result, number: true);
    }

    public async Task<WriteOutcome> MergePullRequestAsync(string? repo, int number, string? method, CancellationToken ct)
    {
        RequireWrite();
        if (!Options.AllowMerge)
        {
            throw Forbidden("Merging pull requests is not enabled on this server.");
        }

        var repository = access.Resolve(access.ConfiguredOwner, repo);
        var mergeMethod = method?.Trim().ToLowerInvariant() switch
        {
            null or "" or "merge" => "merge",
            "squash" => "squash",
            "rebase" => "rebase",
            _ => throw Invalid("'method' must be 'merge', 'squash' or 'rebase'.")
        };

        var result = await writer.SendAsync(HttpMethod.Put, $"{repository.ApiPath}/pulls/{Number(number)}/merge",
            new JsonObject { ["merge_method"] = mergeMethod }, ct);
        return new WriteOutcome(result["merged"]?.GetValue<bool>() == true ? "pull request merged" : "pull request not merged",
            repository.ToString(), number, null, null);
    }

    public async Task<WriteOutcome> DeleteFileAsync(string? repo, string? path, string? message, string? branch, CancellationToken ct)
    {
        RequireDelete();
        var repository = access.Resolve(access.ConfiguredOwner, repo);
        var safePath = sensitivePaths.EnsureReadable(path);
        var branchName = GitHubInput.RequireRef(branch, "branch");
        await EnsureNotProtectedAsync(repository, branchName, "delete files on", ct);
        var commitMessage = Text(message, "message", 500, required: true);

        var url = $"{repository.ApiPath}/contents/{EscapeFilePath(safePath)}";
        var sha = Str((await writer.SendAsync(HttpMethod.Get, $"{url}?ref={Uri.EscapeDataString(branchName)}", null, ct))["sha"])
                  ?? throw new IntegrationException(IntegrationErrorKind.NotFound, GitHubModule.IntegrationName, "File not found.");

        var result = await writer.SendAsync(HttpMethod.Delete, url,
            new JsonObject { ["message"] = commitMessage, ["sha"] = sha, ["branch"] = branchName }, ct);
        return new WriteOutcome("file deleted", repository.ToString(), null, $"{branchName}:{safePath}", Str(result["commit"]?["html_url"]));
    }

    public async Task<WriteOutcome> DeleteBranchAsync(string? repo, string? branch, CancellationToken ct)
    {
        RequireDelete();
        var repository = access.Resolve(access.ConfiguredOwner, repo);
        var name = GitHubInput.RequireRef(branch, "branch");
        await EnsureNotProtectedAsync(repository, name, "delete", ct, force: true);
        await writer.SendAsync(HttpMethod.Delete, $"{repository.ApiPath}/git/refs/heads/{EscapePath(name)}", null, ct);
        return new WriteOutcome("branch deleted", repository.ToString(), null, name, null);
    }

    // ---------- helpers ----------

    private void RequireWrite()
    {
        if (!Options.AllowWrite)
        {
            throw Forbidden("Write operations are not enabled on this server.");
        }
    }

    private void RequireDelete()
    {
        RequireWrite();
        if (!Options.AllowDelete)
        {
            throw Forbidden("Delete operations are not enabled on this server.");
        }
    }

    private async Task EnsureNotProtectedAsync(RepositoryRef repository, string branch, string verb, CancellationToken ct, bool force = false)
    {
        if (Options.AllowCommitToProtectedBranches && !force)
        {
            return;
        }

        var protectedByName = Options.ProtectedBranches.Contains(branch, StringComparer.OrdinalIgnoreCase);
        if (protectedByName || string.Equals(branch, await DefaultBranchAsync(repository, ct), StringComparison.OrdinalIgnoreCase))
        {
            throw Forbidden($"The branch '{branch}' is protected: this server does not {verb} it. Use a separate branch and a pull request.");
        }
    }

    private async Task<string> DefaultBranchAsync(RepositoryRef repository, CancellationToken ct) =>
        (await reader.GetAsync<ApiRepo>(repository.ApiPath, ct)).DefaultBranch;

    private static WriteOutcome Outcome(string action, RepositoryRef repository, JsonNode result, bool number) =>
        new(action, repository.ToString(), number ? result["number"]?.GetValue<int>() : null, null, Str(result["html_url"]));

    private static List<string> Labels(IReadOnlyList<string>? labels)
    {
        var list = (labels ?? []).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).Distinct().ToList();
        if (list.Count > 10 || list.Any(l => l.Length > 50 || l.Any(char.IsControl)))
        {
            throw Invalid("At most 10 labels of up to 50 characters are allowed.");
        }

        return list;
    }

    private static string Text(string? value, string field, int max, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return required ? throw Invalid($"'{field}' is required.") : string.Empty;
        }

        if (value.Length > max)
        {
            throw Invalid($"'{field}' is longer than {max} characters.");
        }

        SecretScanner.EnsureClean(value, field);
        return value;
    }

    private static int Number(int number) =>
        number > 0 ? number : throw Invalid("'number' must be a positive issue or pull request number.");

    private static string EscapePath(string value) => string.Join('/', value.Split('/').Select(Uri.EscapeDataString));

    private static string EscapeFilePath(string value) => EscapePath(value);

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static IntegrationException Invalid(string message) =>
        new(IntegrationErrorKind.InvalidInput, GitHubModule.IntegrationName, message);

    private static IntegrationException Forbidden(string message) =>
        new(IntegrationErrorKind.Forbidden, GitHubModule.IntegrationName, message);
}
