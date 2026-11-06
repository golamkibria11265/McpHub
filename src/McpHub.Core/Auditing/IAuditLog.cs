namespace McpHub.Core.Auditing;

public enum AuditOutcome
{
    Success,
    Denied,
    Failed
}

/// <summary>
/// One tool invocation. Deliberately contains NO arguments and NO results: they may hold personal or
/// payment data, and the audit trail must never become a second copy of it.
/// </summary>
public sealed record AuditEntry(
    DateTimeOffset Timestamp,
    string UserId,
    string Tool,
    AuditOutcome Outcome,
    double DurationMs,
    string? ErrorCode);

public interface IAuditLog
{
    void Record(AuditEntry entry);
}
