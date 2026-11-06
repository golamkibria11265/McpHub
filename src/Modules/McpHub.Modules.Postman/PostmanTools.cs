using System.ComponentModel;
using McpHub.Core.Tools;
using ModelContextProtocol.Server;

namespace McpHub.Modules.Postman;

/// <summary>MCP-facing adapter only: describes the tools and delegates. Rules live in PostmanService.</summary>
[McpServerToolType]
public sealed class PostmanTools(PostmanService service, IToolExecutor executor)
{
    private const string WsDoc = "Workspace id from configuration. Optional when only one workspace is configured.";

    [McpServerTool(Name = "postman_list_workspaces", ReadOnly = true, Idempotent = true)]
    [Description("Lists the Postman workspaces this server is configured to use.")]
    public Task<IReadOnlyList<WorkspaceInfo>> ListWorkspaces(CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("postman_list_workspaces", PostmanScopes.Read, () => service.ListWorkspacesAsync(cancellationToken));

    [McpServerTool(Name = "postman_list_collections", ReadOnly = true, Idempotent = true)]
    [Description("Lists collections in the configured workspace.")]
    public Task<ItemList> ListCollections(
        [Description(WsDoc)] string? workspace = null,
        [Description("Maximum number of items (server enforces an upper limit).")] int? count = null,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("postman_list_collections", PostmanScopes.Read, () => service.ListCollectionsAsync(workspace, count, cancellationToken));

    [McpServerTool(Name = "postman_get_collection", ReadOnly = true, Idempotent = true)]
    [Description("Reads a collection. By default returns a compact outline (folders, request names, methods, URLs). Secrets are always masked.")]
    public Task<CollectionResult> GetCollection(
        [Description("Collection uid from postman_list_collections.")] string uid,
        [Description("True (default) for the outline; false for the full collection JSON.")] bool outline = true,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("postman_get_collection", PostmanScopes.Read, () => service.GetCollectionAsync(uid, outline, cancellationToken));

    [McpServerTool(Name = "postman_list_environments", ReadOnly = true, Idempotent = true)]
    [Description("Lists environments in the configured workspace.")]
    public Task<ItemList> ListEnvironments(
        [Description(WsDoc)] string? workspace = null,
        [Description("Maximum number of items (server enforces an upper limit).")] int? count = null,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("postman_list_environments", PostmanScopes.Read, () => service.ListEnvironmentsAsync(workspace, count, cancellationToken));

    [McpServerTool(Name = "postman_get_environment", ReadOnly = true, Idempotent = true)]
    [Description("Reads an environment and its variables. Secret values are masked.")]
    public Task<EnvironmentResult> GetEnvironment(
        [Description("Environment uid from postman_list_environments.")] string uid,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("postman_get_environment", PostmanScopes.Read, () => service.GetEnvironmentAsync(uid, cancellationToken));

    [McpServerTool(Name = "postman_create_collection", Destructive = false, Idempotent = false)]
    [Description("Creates a collection. Pass only a name for an empty one, or collection JSON (Postman v2.1 format). Secrets must be {{variable}} references; real secrets are rejected. Requires writes to be enabled.")]
    public Task<WriteResult> CreateCollection(
        [Description("Collection name.")] string name,
        [Description("Optional Postman collection v2.1 JSON object as text.")] string? collectionJson = null,
        [Description(WsDoc)] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("postman_create_collection", PostmanScopes.Write, () => service.CreateCollectionAsync(workspace, name, collectionJson, cancellationToken));

    [McpServerTool(Name = "postman_update_collection", Destructive = true, Idempotent = true)]
    [Description("Replaces a whole collection with the given JSON. Read the full collection first (outline=false) and send the complete modified version. Masked or secret values are rejected. Requires writes to be enabled.")]
    public Task<WriteResult> UpdateCollection(
        [Description("Collection uid.")] string uid,
        [Description("Complete Postman collection v2.1 JSON object as text.")] string collectionJson,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("postman_update_collection", PostmanScopes.Write, () => service.UpdateCollectionAsync(uid, collectionJson, cancellationToken));

    [McpServerTool(Name = "postman_delete_collection", Destructive = true, Idempotent = true)]
    [Description("Permanently deletes a collection. Requires deletes to be enabled. Ask the user to confirm first.")]
    public Task<WriteResult> DeleteCollection(
        [Description("Collection uid.")] string uid,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("postman_delete_collection", PostmanScopes.Delete, () => service.DeleteCollectionAsync(uid, cancellationToken));

    [McpServerTool(Name = "postman_create_environment", Destructive = false, Idempotent = false)]
    [Description("Creates an environment with plain variables. Secrets cannot be written here; put them in Postman directly. Requires writes to be enabled.")]
    public Task<WriteResult> CreateEnvironment(
        [Description("Environment name.")] string name,
        [Description("Variables to set.")] List<EnvironmentVariableInput>? variables = null,
        [Description(WsDoc)] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("postman_create_environment", PostmanScopes.Write, () => service.CreateEnvironmentAsync(workspace, name, variables, cancellationToken));

    [McpServerTool(Name = "postman_update_environment", Destructive = false, Idempotent = true)]
    [Description("Adds or changes the given variables (matched by key) and leaves all other variables untouched. Secret variables cannot be changed. Requires writes to be enabled.")]
    public Task<WriteResult> UpdateEnvironment(
        [Description("Environment uid.")] string uid,
        [Description("Variables to add or change.")] List<EnvironmentVariableInput>? variables = null,
        [Description("Optional new environment name.")] string? name = null,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("postman_update_environment", PostmanScopes.Write, () => service.UpdateEnvironmentAsync(uid, name, variables, cancellationToken));

    [McpServerTool(Name = "postman_delete_environment", Destructive = true, Idempotent = true)]
    [Description("Permanently deletes an environment. Requires deletes to be enabled. Ask the user to confirm first.")]
    public Task<WriteResult> DeleteEnvironment(
        [Description("Environment uid.")] string uid,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync("postman_delete_environment", PostmanScopes.Delete, () => service.DeleteEnvironmentAsync(uid, cancellationToken));
}
