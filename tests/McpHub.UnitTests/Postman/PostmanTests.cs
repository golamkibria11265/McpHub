using System.Text.Json.Nodes;
using McpHub.Core.Errors;
using McpHub.Modules.Postman;
using Microsoft.Extensions.Options;

namespace McpHub.UnitTests.Postman;

public sealed class FakePostmanApi : IPostmanApiClient
{
    public List<(HttpMethod Method, string Path, JsonNode? Body)> Calls { get; } = [];

    public Func<HttpMethod, string, JsonNode> Respond { get; set; } = (_, _) => new JsonObject();

    public Task<JsonNode> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        Calls.Add((method, path, body?.DeepClone()));
        return Task.FromResult(Respond(method, path));
    }
}

public sealed class SecretPolicyTests
{
    private static readonly SecretPolicy Policy = new();

    [Theory]
    [InlineData("password")]
    [InlineData("Authorization")]
    [InlineData("x-api-key")]
    [InlineData("client_secret")]
    [InlineData("access_token")]
    [InlineData("pan")]
    public void Sensitive_names_are_detected(string name) => Assert.True(Policy.IsSensitiveName(name));

    [Theory]
    [InlineData("baseUrl")]
    [InlineData("Content-Type")]
    [InlineData("name")]
    public void Ordinary_names_are_not_sensitive(string name) => Assert.False(Policy.IsSensitiveName(name));

    [Fact]
    public void Redact_masks_header_and_variable_values_but_keeps_references()
    {
        var node = JsonNode.Parse("""
            {"item":[{"request":{"header":[{"key":"Authorization","value":"Bearer abc"},{"key":"X-Other","value":"{{token}}"},{"key":"Accept","value":"json"}],
            "url":{"raw":"https://x.test/a?api_key=zzz&page=2"}}}],
            "variable":[{"key":"k","value":"v","type":"secret"}]}
            """)!;
        var count = Policy.Redact(node);
        Assert.Equal(3, count);
        var text = node.ToJsonString();
        Assert.False(text.Contains("Bearer abc"));
        Assert.False(text.Contains("zzz"));
        Assert.Contains("{{token}}", text);
        Assert.Contains("page=2", text);
        Assert.Contains("json", text);
    }

    [Fact]
    public void FindSecret_blocks_real_secrets_and_mask_but_allows_references()
    {
        Assert.NotNull(Policy.FindSecret(JsonNode.Parse("""{"header":[{"key":"Authorization","value":"Bearer abc"}]}""")));
        Assert.NotNull(Policy.FindSecret(JsonNode.Parse("""{"name":"[REDACTED]"}""")));
        Assert.NotNull(Policy.FindSecret(JsonNode.Parse("""{"url":{"raw":"https://x/y?token=abc"}}""")));
        Assert.Null(Policy.FindSecret(JsonNode.Parse("""{"header":[{"key":"Authorization","value":"{{token}}"}],"url":{"raw":"https://x/y?token={{token}}"}}""")));
    }
}

public sealed class PostmanServiceTests
{
    private static PostmanService Create(FakePostmanApi api, bool write = false, bool delete = false, params string[] workspaces)
    {
        var options = Options.Create(new PostmanOptions
        {
            AllowedWorkspaceIds = workspaces.Length == 0 ? ["ws-1"] : workspaces,
            AllowWrite = write,
            AllowDelete = delete
        });
        return new PostmanService(api, new PostmanAccess(options), new SecretPolicy(), options);
    }

    private static FakePostmanApi ApiWithCollection(string uid = "c-1") => new()
    {
        Respond = (_, path) => path.StartsWith("collections?workspace=", StringComparison.Ordinal)
            ? JsonNode.Parse($$"""{"collections":[{"uid":"{{uid}}","name":"A"}]}""")!
            : JsonNode.Parse("""{"collection":{"info":{"name":"A"},"item":[{"name":"r","request":{"method":"GET","url":"https://x/y?token=abc","header":[{"key":"Authorization","value":"Bearer abc"}]}}]}}""")!
    };

    [Fact]
    public async Task Writes_are_refused_by_default()
    {
        var ex = await Assert.ThrowsAsync<IntegrationException>(() => Create(new FakePostmanApi()).CreateCollectionAsync(null, "n", null, default));
        Assert.Equal(IntegrationErrorKind.Forbidden, ex.Kind);
    }

    [Fact]
    public async Task Delete_needs_its_own_switch()
    {
        var api = ApiWithCollection();
        var ex = await Assert.ThrowsAsync<IntegrationException>(() => Create(api, write: true).DeleteCollectionAsync("c-1", default));
        Assert.Equal(IntegrationErrorKind.Forbidden, ex.Kind);
        Assert.Equal(0, api.Calls.Count);
    }

    [Fact]
    public async Task Unknown_workspace_is_refused()
    {
        var ex = await Assert.ThrowsAsync<IntegrationException>(() => Create(new FakePostmanApi()).ListCollectionsAsync("other", null, default));
        Assert.Equal(IntegrationErrorKind.Forbidden, ex.Kind);
    }

    [Fact]
    public async Task Several_workspaces_require_a_choice()
    {
        var ex = await Assert.ThrowsAsync<IntegrationException>(() => Create(new FakePostmanApi(), false, false, "a", "b").ListCollectionsAsync(null, null, default));
        Assert.Equal(IntegrationErrorKind.InvalidInput, ex.Kind);
    }

    [Fact]
    public async Task Collection_outside_allowed_workspaces_is_not_found()
    {
        var api = ApiWithCollection("c-1");
        var ex = await Assert.ThrowsAsync<IntegrationException>(() => Create(api).GetCollectionAsync("c-999", true, default));
        Assert.Equal(IntegrationErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task Invalid_ids_never_reach_the_api()
    {
        var api = new FakePostmanApi();
        await Assert.ThrowsAsync<IntegrationException>(() => Create(api).GetCollectionAsync("../users", true, default));
        Assert.Equal(0, api.Calls.Count);
    }

    [Fact]
    public async Task Full_collection_is_masked_and_outline_hides_secret_query()
    {
        var full = await Create(ApiWithCollection()).GetCollectionAsync("c-1", false, default);
        var text = full.Content!.ToJsonString();
        Assert.False(text.Contains("Bearer abc"));
        Assert.False(text.Contains("token=abc"));
        Assert.True(full.MaskedValues >= 2);

        var outline = await Create(ApiWithCollection()).GetCollectionAsync("c-1", true, default);
        Assert.False(outline.Content!.ToJsonString().Contains("abc"));
    }

    [Fact]
    public async Task Update_rejects_masked_or_secret_payloads_without_calling_put()
    {
        var api = ApiWithCollection();
        var svc = Create(api, write: true);
        var ex = await Assert.ThrowsAsync<IntegrationException>(() =>
            svc.UpdateCollectionAsync("c-1", """{"info":{"name":"A"},"item":[{"name":"r","request":{"header":[{"key":"Authorization","value":"[REDACTED]"}]}}]}""", default));
        Assert.Equal(IntegrationErrorKind.InvalidInput, ex.Kind);
        Assert.False(api.Calls.Any(c => c.Method == HttpMethod.Put));
    }

    [Fact]
    public async Task Create_collection_with_only_a_name_posts_a_minimal_collection()
    {
        var api = new FakePostmanApi { Respond = (_, _) => JsonNode.Parse("""{"collection":{"uid":"n-1","name":"N"}}""")! };
        var result = await Create(api, write: true).CreateCollectionAsync(null, "N", null, default);
        Assert.Equal("n-1", result.Uid);
        var call = api.Calls.Single();
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("collections?workspace=ws-1", call.Path);
        Assert.Equal("N", call.Body!["collection"]!["info"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Environment_update_merges_and_keeps_existing_secrets()
    {
        var api = new FakePostmanApi
        {
            Respond = (method, path) => path.StartsWith("environments?workspace=", StringComparison.Ordinal)
                ? JsonNode.Parse("""{"environments":[{"uid":"e-1","name":"E"}]}""")!
                : JsonNode.Parse("""{"environment":{"name":"E","values":[{"key":"pw","value":"real","type":"secret","enabled":true},{"key":"host","value":"a","type":"default","enabled":true}]}}""")!
        };
        await Create(api, write: true).UpdateEnvironmentAsync("e-1", null,
            [new EnvironmentVariableInput { Key = "host", Value = "b" }, new EnvironmentVariableInput { Key = "extra", Value = "1" }], default);
        var put = api.Calls.Single(c => c.Method == HttpMethod.Put).Body!["environment"]!["values"]!.AsArray();
        Assert.Equal(3, put.Count);
        Assert.Equal("real", put[0]!["value"]!.GetValue<string>());
        Assert.Equal("b", put[1]!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task Writing_a_secret_variable_is_refused()
    {
        var ex = await Assert.ThrowsAsync<IntegrationException>(() =>
            Create(new FakePostmanApi(), write: true).CreateEnvironmentAsync(null, "E", [new EnvironmentVariableInput { Key = "api_key", Value = "abc" }], default));
        Assert.Equal(IntegrationErrorKind.InvalidInput, ex.Kind);
    }

    [Fact]
    public async Task Reading_an_environment_masks_secret_values()
    {
        var api = new FakePostmanApi
        {
            Respond = (_, path) => path.StartsWith("environments?workspace=", StringComparison.Ordinal)
                ? JsonNode.Parse("""{"environments":[{"uid":"e-1","name":"E"}]}""")!
                : JsonNode.Parse("""{"environment":{"name":"E","values":[{"key":"pw","value":"real","type":"secret"},{"key":"host","value":"a"}]}}""")!
        };
        var env = await Create(api).GetEnvironmentAsync("e-1", default);
        Assert.Equal("[REDACTED]", env.Variables[0].Value);
        Assert.Equal("a", env.Variables[1].Value);
        Assert.Equal(1, env.MaskedValues);
    }
}
