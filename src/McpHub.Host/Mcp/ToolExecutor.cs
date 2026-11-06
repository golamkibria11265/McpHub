using System.Diagnostics;
using McpHub.Core.Auditing;
using McpHub.Core.Errors;
using McpHub.Core.Security;
using McpHub.Core.Tools;
using ModelContextProtocol;

namespace McpHub.Host.Mcp;

/// <summary>
/// Scope check, audit trail and error translation for every tool call.
/// Only <see cref="IntegrationException"/> messages are shown to the caller; anything unexpected is logged
/// server-side and replaced by a generic message, so internals never leak to the model or the user.
/// </summary>
internal sealed class ToolExecutor(
    ICallerContext caller,
    IAuditLog audit,
    ILogger<ToolExecutor> logger) : IToolExecutor
{
    public async Task<T> ExecuteAsync<T>(string toolName, string requiredScope, Func<Task<T>> action)
    {
        var started = Stopwatch.GetTimestamp();

        if (!caller.HasScope(requiredScope))
        {
            Record(toolName, started, AuditOutcome.Denied, "missing_scope");
            throw new McpException($"Access denied: this tool requires the '{requiredScope}' scope.");
        }

        try
        {
            var result = await action();
            Record(toolName, started, AuditOutcome.Success, null);
            return result;
        }
        catch (IntegrationException ex)
        {
            Record(toolName, started, AuditOutcome.Failed, ex.Kind.ToString());
            throw new McpException($"[{ex.Integration}] {ex.Message}", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected failure in tool {Tool}", toolName);
            Record(toolName, started, AuditOutcome.Failed, "unexpected");
            throw new McpException($"The tool '{toolName}' failed due to an internal error.");
        }
    }

    private void Record(string tool, long started, AuditOutcome outcome, string? errorCode) =>
        audit.Record(new AuditEntry(
            DateTimeOffset.UtcNow,
            caller.UserId,
            tool,
            outcome,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            errorCode));
}
