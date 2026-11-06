using McpHub.Core.Auditing;
using Microsoft.Extensions.Logging;

namespace McpHub.Infrastructure.Auditing;

/// <summary>
/// Writes audit entries as structured log events under the "McpHub.Audit" category, so they can be routed to a
/// separate, tamper-resistant sink (journald forwarding, SIEM) purely through logging configuration.
/// </summary>
internal sealed class LoggerAuditLog(ILoggerFactory loggerFactory) : IAuditLog
{
    private readonly ILogger _logger = loggerFactory.CreateLogger("McpHub.Audit");

    public void Record(AuditEntry entry) =>
        _logger.LogInformation(
            "tool_call user={UserId} tool={Tool} outcome={Outcome} duration_ms={DurationMs} error={ErrorCode}",
            entry.UserId,
            entry.Tool,
            entry.Outcome,
            Math.Round(entry.DurationMs, 1),
            entry.ErrorCode ?? "none");
}
