using System.Text.Json;
using System.Text.Json.Nodes;
using McpHub.Core.Errors;
using Microsoft.Extensions.Options;

namespace McpHub.Modules.Postman;

public sealed class PostmanService(
    IPostmanApiClient api,
    PostmanAccess access,
    SecretPolicy secrets,
    IOptions<PostmanOptions> options)
{
    private const string Schema = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json";

    private PostmanOptions Options => options.Value;

    // ---------- reads ----------

    public async Task<IReadOnlyList<WorkspaceInfo>> ListWorkspacesAsync(CancellationToken ct)
    {
        var result = new List<WorkspaceInfo>();
        foreach (var id in access.Workspaces)
        {
            var node = await api.SendAsync(HttpMethod.Get, $"workspaces/{PostmanAccess.SafeId(id, "workspace")}", null, ct);
            var w = node["workspace"];
            result.Add(new WorkspaceInfo(id, Str(w?["name"]), Str(w?["type"]), Str(w?["visibility"])));
        }

        return result;
    }

    public async Task<ItemList> ListCollectionsAsync(string? workspace, int? count, CancellationToken ct)
    {
        var ws = access.ResolveWorkspace(workspace);
        return ToList(ws, await CollectionsOfAsync(ws, ct), "collections", count);
    }

    public async Task<ItemList> ListEnvironmentsAsync(string? workspace, int? count, CancellationToken ct)
    {
        var ws = access.ResolveWorkspace(workspace);
        return ToList(ws, await EnvironmentsOfAsync(ws, ct), "environments", count);
    }

    public async Task<CollectionResult> GetCollectionAsync(string uid, bool outline, CancellationToken ct)
    {
        uid = PostmanAccess.SafeId(uid, "uid");
        await EnsureInAllowedWorkspaceAsync(uid, collection: true, ct);
        var node = await api.SendAsync(HttpMethod.Get, $"collections/{uid}", null, ct);
        var collection = node["collection"] ?? throw PostmanAccess.Fail(IntegrationErrorKind.Unknown, "Postman returned no collection.");
        var name = Str(collection["info"]?["name"]);

        if (outline)
        {
            var summary = new JsonObject { ["name"] = name, ["items"] = Outline(collection["item"] as JsonArray) };
            return new CollectionResult(uid, name, true, 0, summary);
        }

        var masked = secrets.Redact(collection);
        if (collection.ToJsonString().Length > Options.MaxResponseChars)
        {
            throw PostmanAccess.Fail(IntegrationErrorKind.InvalidInput,
                "The collection is too large to return in full. Read the outline instead.");
        }

        return new CollectionResult(uid, name, false, masked, collection);
    }

    public async Task<EnvironmentResult> GetEnvironmentAsync(string uid, CancellationToken ct)
    {
        uid = PostmanAccess.SafeId(uid, "uid");
        await EnsureInAllowedWorkspaceAsync(uid, collection: false, ct);
        var node = await api.SendAsync(HttpMethod.Get, $"environments/{uid}", null, ct);
        var env = node["environment"] ?? throw PostmanAccess.Fail(IntegrationErrorKind.Unknown, "Postman returned no environment.");

        var variables = new List<EnvironmentVariable>();
        var masked = 0;
        foreach (var v in (env["values"] as JsonArray) ?? [])
        {
            var key = Str(v?["key"]) ?? string.Empty;
            var type = Str(v?["type"]) ?? "default";
            var value = Str(v?["value"]);
            var hide = secrets.IsSensitiveVariable(key, type) && !SecretPolicy.IsHarmless(value);
            if (hide)
            {
                masked++;
            }

            variables.Add(new EnvironmentVariable(key, hide ? SecretPolicy.Mask : value, type, v?["enabled"]?.GetValue<bool>() ?? true, hide));
        }

        return new EnvironmentResult(uid, Str(env["name"]), variables, masked);
    }

    // ---------- writes ----------

    public async Task<WriteResult> CreateCollectionAsync(string? workspace, string name, string? collectionJson, CancellationToken ct)
    {
        access.RequireWrite();
        var ws = access.ResolveWorkspace(workspace);
        var collection = ParseCollection(name, collectionJson);
        var node = await api.SendAsync(HttpMethod.Post, $"collections?workspace={PostmanAccess.SafeId(ws, "workspace")}",
            new JsonObject { ["collection"] = collection }, ct);
        return new WriteResult("created", Str(node["collection"]?["uid"]) ?? string.Empty, Str(node["collection"]?["name"]) ?? name);
    }

    public async Task<WriteResult> UpdateCollectionAsync(string uid, string collectionJson, CancellationToken ct)
    {
        access.RequireWrite();
        uid = PostmanAccess.SafeId(uid, "uid");
        await EnsureInAllowedWorkspaceAsync(uid, collection: true, ct);
        var collection = ParseCollection(null, collectionJson);
        var node = await api.SendAsync(HttpMethod.Put, $"collections/{uid}", new JsonObject { ["collection"] = collection }, ct);
        return new WriteResult("updated", uid, Str(node["collection"]?["name"]));
    }

    public async Task<WriteResult> DeleteCollectionAsync(string uid, CancellationToken ct)
    {
        access.RequireDelete();
        uid = PostmanAccess.SafeId(uid, "uid");
        await EnsureInAllowedWorkspaceAsync(uid, collection: true, ct);
        await api.SendAsync(HttpMethod.Delete, $"collections/{uid}", null, ct);
        return new WriteResult("deleted", uid, null);
    }

    public async Task<WriteResult> CreateEnvironmentAsync(string? workspace, string name, IReadOnlyList<EnvironmentVariableInput>? variables, CancellationToken ct)
    {
        access.RequireWrite();
        var ws = access.ResolveWorkspace(workspace);
        var values = new JsonArray();
        foreach (var v in Validate(variables))
        {
            values.Add(ToValue(v));
        }

        var node = await api.SendAsync(HttpMethod.Post, $"environments?workspace={PostmanAccess.SafeId(ws, "workspace")}",
            new JsonObject { ["environment"] = new JsonObject { ["name"] = RequireName(name), ["values"] = values } }, ct);
        return new WriteResult("created", Str(node["environment"]?["uid"]) ?? string.Empty, Str(node["environment"]?["name"]) ?? name);
    }

    /// <summary>Upserts the given variables by key and leaves every other variable (including secrets) exactly as it is.</summary>
    public async Task<WriteResult> UpdateEnvironmentAsync(string uid, string? name, IReadOnlyList<EnvironmentVariableInput>? variables, CancellationToken ct)
    {
        access.RequireWrite();
        uid = PostmanAccess.SafeId(uid, "uid");
        await EnsureInAllowedWorkspaceAsync(uid, collection: false, ct);
        var inputs = Validate(variables);

        var current = (await api.SendAsync(HttpMethod.Get, $"environments/{uid}", null, ct))["environment"]
                      ?? throw PostmanAccess.Fail(IntegrationErrorKind.Unknown, "Postman returned no environment.");
        var values = current["values"] as JsonArray ?? [];

        foreach (var input in inputs)
        {
            var existing = values.OfType<JsonObject>().FirstOrDefault(v =>
                string.Equals(Str(v["key"]), input.Key, StringComparison.Ordinal));
            if (existing is not null && string.Equals(Str(existing["type"]), "secret", StringComparison.OrdinalIgnoreCase))
            {
                throw PostmanAccess.Fail(IntegrationErrorKind.Forbidden, $"Variable '{input.Key}' is a secret and cannot be changed through this server.");
            }

            if (existing is null)
            {
                values.Add(ToValue(input));
            }
            else
            {
                existing["value"] = input.Value ?? string.Empty;
                if (input.Enabled is { } enabled)
                {
                    existing["enabled"] = enabled;
                }
            }
        }

        var body = new JsonObject { ["name"] = string.IsNullOrWhiteSpace(name) ? Str(current["name"]) : name.Trim(), ["values"] = values.DeepClone() };
        var node = await api.SendAsync(HttpMethod.Put, $"environments/{uid}", new JsonObject { ["environment"] = body }, ct);
        return new WriteResult("updated", uid, Str(node["environment"]?["name"]));
    }

    public async Task<WriteResult> DeleteEnvironmentAsync(string uid, CancellationToken ct)
    {
        access.RequireDelete();
        uid = PostmanAccess.SafeId(uid, "uid");
        await EnsureInAllowedWorkspaceAsync(uid, collection: false, ct);
        await api.SendAsync(HttpMethod.Delete, $"environments/{uid}", null, ct);
        return new WriteResult("deleted", uid, null);
    }

    // ---------- helpers ----------

    private async Task<JsonNode> CollectionsOfAsync(string ws, CancellationToken ct) =>
        await api.SendAsync(HttpMethod.Get, $"collections?workspace={PostmanAccess.SafeId(ws, "workspace")}", null, ct);

    private async Task<JsonNode> EnvironmentsOfAsync(string ws, CancellationToken ct) =>
        await api.SendAsync(HttpMethod.Get, $"environments?workspace={PostmanAccess.SafeId(ws, "workspace")}", null, ct);

    /// <summary>An id is only usable when it appears in the listing of an allowed workspace. Blocks ids from elsewhere.</summary>
    internal async Task EnsureInAllowedWorkspaceAsync(string uid, bool collection, CancellationToken ct)
    {
        foreach (var ws in access.Workspaces)
        {
            var listing = collection ? await CollectionsOfAsync(ws, ct) : await EnvironmentsOfAsync(ws, ct);
            var items = listing[collection ? "collections" : "environments"] as JsonArray ?? [];
            if (items.Any(i => string.Equals(Str(i?["uid"]), uid, StringComparison.OrdinalIgnoreCase)
                               || string.Equals(Str(i?["id"]), uid, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
        }

        throw PostmanAccess.Fail(IntegrationErrorKind.NotFound, "Not found in the allowed workspaces.");
    }

    private ItemList ToList(string workspace, JsonNode listing, string key, int? count)
    {
        var limit = Options.ClampCount(count);
        var all = (listing[key] as JsonArray ?? []).Select(i => new ItemInfo(Str(i?["uid"]) ?? Str(i?["id"]) ?? string.Empty, Str(i?["name"]), Str(i?["updatedAt"]))).ToList();
        return new ItemList(workspace, all.Take(limit).ToList(), all.Count > limit);
    }

    private JsonNode ParseCollection(string? name, string? json)
    {
        JsonNode collection;
        if (string.IsNullOrWhiteSpace(json))
        {
            collection = new JsonObject
            {
                ["info"] = new JsonObject { ["name"] = RequireName(name), ["schema"] = Schema },
                ["item"] = new JsonArray()
            };
        }
        else
        {
            if (json.Length > Options.MaxWriteChars)
            {
                throw PostmanAccess.Fail(IntegrationErrorKind.InvalidInput, "The collection is too large.");
            }

            try
            {
                collection = JsonNode.Parse(json) as JsonObject
                             ?? throw PostmanAccess.Fail(IntegrationErrorKind.InvalidInput, "The collection must be a JSON object.");
            }
            catch (JsonException)
            {
                throw PostmanAccess.Fail(IntegrationErrorKind.InvalidInput, "The collection is not valid JSON.");
            }

            if (collection["info"] is not JsonObject info || string.IsNullOrWhiteSpace(Str(info["name"])))
            {
                if (name is null)
                {
                    throw PostmanAccess.Fail(IntegrationErrorKind.InvalidInput, "The collection needs info.name.");
                }

                collection["info"] = new JsonObject { ["name"] = RequireName(name), ["schema"] = Schema };
            }
            else if (info["schema"] is null)
            {
                info["schema"] = Schema;
            }
        }

        if (secrets.FindSecret(collection) is { } found)
        {
            throw PostmanAccess.Fail(IntegrationErrorKind.InvalidInput,
                $"The collection contains a secret or masked value ({found}). Use a {{{{variable}}}} reference instead; secrets cannot be written through this server.");
        }

        return collection;
    }

    private IReadOnlyList<EnvironmentVariableInput> Validate(IReadOnlyList<EnvironmentVariableInput>? variables)
    {
        var list = variables ?? [];
        if (list.Count > 200)
        {
            throw PostmanAccess.Fail(IntegrationErrorKind.InvalidInput, "Too many variables in one call (limit 200).");
        }

        foreach (var v in list)
        {
            if (string.IsNullOrWhiteSpace(v.Key) || v.Key.Length > 200)
            {
                throw PostmanAccess.Fail(IntegrationErrorKind.InvalidInput, "Each variable needs a key (up to 200 characters).");
            }

            if (v.Value is { } value && (value.Contains(SecretPolicy.Mask, StringComparison.Ordinal)
                    || (secrets.IsSensitiveName(v.Key) && !SecretPolicy.IsHarmless(value))))
            {
                throw PostmanAccess.Fail(IntegrationErrorKind.InvalidInput,
                    $"Variable '{v.Key}' looks like a secret. Secrets cannot be written through this server; set it in Postman directly.");
            }
        }

        return list;
    }

    private static JsonObject ToValue(EnvironmentVariableInput v) => new()
    {
        ["key"] = v.Key.Trim(),
        ["value"] = v.Value ?? string.Empty,
        ["type"] = "default",
        ["enabled"] = v.Enabled ?? true
    };

    private static string RequireName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 200
            ? name.Trim()
            : throw PostmanAccess.Fail(IntegrationErrorKind.InvalidInput, "A name (up to 200 characters) is required.");

    private JsonArray Outline(JsonArray? items)
    {
        var result = new JsonArray();
        foreach (var item in items ?? [])
        {
            if (item is not JsonObject obj)
            {
                continue;
            }

            var entry = new JsonObject { ["name"] = Str(obj["name"]) };
            if (obj["request"] is JsonObject request)
            {
                entry["method"] = Str(request["method"]);
                var url = request["url"] is JsonObject u ? Str(u["raw"]) : Str(request["url"]);
                entry["url"] = url is null ? null : secrets.MaskUrl(url);
            }

            if (obj["item"] is JsonArray children)
            {
                entry["items"] = Outline(children);
            }

            result.Add(entry);
        }

        return result;
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
