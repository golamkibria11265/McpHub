using System.Text.Json.Nodes;
using McpHub.Core.Errors;
using McpHub.Modules.GitHub;
using McpHub.Modules.GitHub.Access;
using McpHub.Modules.GitHub.Clients;
using McpHub.Modules.GitHub.Services;
using Microsoft.Extensions.Options;

namespace McpHub.UnitTests.GitHub;

internal sealed class FakeWriteClient(Func<HttpMethod, string, JsonNode> respond) : IGitHubWriteClient
{
    public List<(HttpMethod Method, string Path, JsonNode? Body)> Calls { get; } = [];

    public Task<JsonNode> SendAsync(HttpMethod method, string relativePath, JsonNode? body, CancellationToken cancellationToken)
    {
        Calls.Add((method, relativePath, body?.DeepClone()));
        return Task.FromResult(respond(method, relativePath));
    }
}

public sealed class SecretScannerTests
{
    [Theory]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nabc")]
    [InlineData("token = ghp_abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("key AKIAABCDEFGHIJKLMNOP")]
    [InlineData("Server=db;User Id=sa;Password=Sup3rS3cret!;")]
    public void Secrets_are_refused(string text) =>
        Assert.Throws<IntegrationException>(() => SecretScanner.EnsureClean(text, "content"));

    [Theory]
    [InlineData("Server=db;Password=[DB_PASSWORD];")]
    [InlineData("Set the password in the vault")]
    [InlineData("var x = 1;")]
    public void Ordinary_text_and_placeholders_pass(string text) => SecretScanner.EnsureClean(text, "content");
}

public sealed class GitHubWriteServiceTests
{
    private static GitHubWriteService Create(FakeWriteClient writer, bool write = true, bool delete = false, bool merge = false)
    {
        var options = Options.Create(new GitHubOptions { AllowedOwners = ["acme"], AllowWrite = write, AllowDelete = delete, AllowMerge = merge });
        var reader = new FakeGitHubApiClient(_ => new McpHub.Modules.GitHub.Models.Api.ApiRepo("acme/r", null, false, false, null, 0, "main", null, "u"));
        return new GitHubWriteService(writer, reader, new RepositoryAccessPolicy(options), new SensitivePathPolicy(), options);
    }

    private static FakeWriteClient Ok() => new((_, _) => JsonNode.Parse("""{"number":7,"html_url":"https://x/7"}""")!);

    [Fact]
    public async Task Writes_are_refused_by_default()
    {
        var writer = Ok();
        var ex = await Assert.ThrowsAsync<IntegrationException>(() => Create(writer, write: false).CreateIssueAsync("r", "t", null, null, default));
        Assert.Equal(IntegrationErrorKind.Forbidden, ex.Kind);
        Assert.Equal(0, writer.Calls.Count);
    }

    [Fact]
    public async Task Create_issue_posts_to_the_configured_owner()
    {
        var writer = Ok();
        var result = await Create(writer).CreateIssueAsync("r", "Title", "Body", ["bug"], default);
        Assert.Equal(7, result.Number);
        var call = writer.Calls.Single();
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("repos/acme/r/issues", call.Path);
    }

    [Fact]
    public async Task Secrets_in_text_never_reach_the_api()
    {
        var writer = Ok();
        await Assert.ThrowsAsync<IntegrationException>(() =>
            Create(writer).AddCommentAsync("r", 1, "use ghp_abcdefghijklmnopqrstuvwxyz0123456789", default));
        Assert.Equal(0, writer.Calls.Count);
    }

    [Fact]
    public async Task Committing_to_the_default_branch_is_refused()
    {
        var writer = Ok();
        var ex = await Assert.ThrowsAsync<IntegrationException>(() =>
            Create(writer).PutFileAsync("r", "src/a.txt", "hi", "msg", "main", default));
        Assert.Equal(IntegrationErrorKind.Forbidden, ex.Kind);
        Assert.Equal(0, writer.Calls.Count);
    }

    [Fact]
    public async Task Sensitive_paths_cannot_be_written()
    {
        var ex = await Assert.ThrowsAsync<IntegrationException>(() =>
            Create(Ok()).PutFileAsync("r", ".env", "A=1", "msg", "feature/x", default));
        Assert.Equal(IntegrationErrorKind.Forbidden, ex.Kind);
    }

    [Fact]
    public async Task Put_file_creates_a_new_file_when_it_does_not_exist()
    {
        var writer = new FakeWriteClient((method, _) => method == HttpMethod.Get
            ? throw new IntegrationException(IntegrationErrorKind.NotFound, "GitHub", "nf")
            : JsonNode.Parse("""{"commit":{"html_url":"https://x/c"}}""")!);
        var result = await Create(writer).PutFileAsync("r", "docs/a.md", "hello", "add", "feature/x", default);
        Assert.Equal("file created", result.Action);
        var put = writer.Calls.Single(c => c.Method == HttpMethod.Put);
        Assert.Equal("feature/x", put.Body!["branch"]!.GetValue<string>());
        Assert.Null(put.Body["sha"]);
    }

    [Fact]
    public async Task Put_file_updates_with_the_current_sha()
    {
        var writer = new FakeWriteClient((method, _) => method == HttpMethod.Get
            ? JsonNode.Parse("""{"type":"file","sha":"abc123"}""")!
            : JsonNode.Parse("""{"commit":{"html_url":"https://x/c"}}""")!);
        var result = await Create(writer).PutFileAsync("r", "docs/a.md", "hello", "edit", "feature/x", default);
        Assert.Equal("file updated", result.Action);
        Assert.Equal("abc123", writer.Calls.Single(c => c.Method == HttpMethod.Put).Body!["sha"]!.GetValue<string>());
    }

    [Fact]
    public async Task Merge_needs_its_own_switch()
    {
        var ex = await Assert.ThrowsAsync<IntegrationException>(() => Create(Ok()).MergePullRequestAsync("r", 3, null, default));
        Assert.Equal(IntegrationErrorKind.Forbidden, ex.Kind);
    }

    [Fact]
    public async Task Deletes_need_their_own_switch_and_never_hit_protected_branches()
    {
        var off = await Assert.ThrowsAsync<IntegrationException>(() => Create(Ok()).DeleteBranchAsync("r", "feature/x", default));
        Assert.Equal(IntegrationErrorKind.Forbidden, off.Kind);

        var writer = Ok();
        var prot = await Assert.ThrowsAsync<IntegrationException>(() => Create(writer, delete: true).DeleteBranchAsync("r", "main", default));
        Assert.Equal(IntegrationErrorKind.Forbidden, prot.Kind);
        Assert.Equal(0, writer.Calls.Count);

        await Create(writer, delete: true).DeleteBranchAsync("r", "feature/x", default);
        Assert.Equal(HttpMethod.Delete, writer.Calls.Single().Method);
        Assert.Equal("repos/acme/r/git/refs/heads/feature/x", writer.Calls.Single().Path);
    }
}
