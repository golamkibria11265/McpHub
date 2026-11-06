namespace McpHub.Modules.Postman;

/// <summary>Bound from <c>Modules:Postman</c>. The API key lives in <c>Modules:Postman:Credentials:Token</c>.</summary>
public sealed class PostmanOptions
{
    /// <summary>"https://api.getpostman.com/" (US) or "https://api.eu.postman.com/" (EU data residency).</summary>
    public string BaseUrl { get; set; } = "https://api.getpostman.com/";

    /// <summary>
    /// The only workspaces the tools may touch. Required: the server never reaches beyond this list even if the
    /// API key could. With exactly one entry the caller does not need to name a workspace at all.
    /// </summary>
    public string[] AllowedWorkspaceIds { get; set; } = [];

    /// <summary>Master switch for create and update tools. Off by default.</summary>
    public bool AllowWrite { get; set; }

    /// <summary>Extra switch for delete tools. Off by default and only effective together with <see cref="AllowWrite"/>.</summary>
    public bool AllowDelete { get; set; }

    /// <summary>Upper bound on list sizes. Keeps responses (and LLM token cost) small.</summary>
    public int MaxResults { get; set; } = 50;

    /// <summary>Largest collection (as JSON text) returned in full; bigger ones must be read as an outline.</summary>
    public int MaxResponseChars { get; set; } = 60_000;

    /// <summary>Largest collection or environment payload accepted from a caller.</summary>
    public int MaxWriteChars { get; set; } = 200_000;

    /// <summary>Extra names (header, variable, field) treated as secrets, on top of the built-in list.</summary>
    public string[] ExtraDeniedKeys { get; set; } = [];

    public int ClampCount(int? requested) => Math.Clamp(requested ?? MaxResults, 1, MaxResults);
}
