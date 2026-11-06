namespace McpHub.Core.Tools;

/// <summary>
/// Wraps every tool call with the cross-cutting concerns: scope check, audit, and safe error translation.
/// Tool classes stay one-liners and never repeat this logic.
/// </summary>
public interface IToolExecutor
{
    /// <param name="toolName">Name exposed to the model; also what appears in the audit log.</param>
    /// <param name="requiredScope">Scope the caller's token must contain, e.g. "github.read".</param>
    /// <param name="action">The actual work.</param>
    Task<T> ExecuteAsync<T>(string toolName, string requiredScope, Func<Task<T>> action);
}
