namespace McpHub.Core.Errors;

public enum IntegrationErrorKind
{
    InvalidInput,
    NotFound,
    Unauthorized,
    Forbidden,
    RateLimited,
    Unavailable,
    Unknown
}

/// <summary>
/// Failure while talking to (or validating a request for) an external system.
/// The message MUST be safe to show to the end user: no tokens, URLs with secrets, or raw upstream payloads.
/// </summary>
public sealed class IntegrationException : Exception
{
    public IntegrationException(IntegrationErrorKind kind, string integration, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Integration = integration;
    }

    public IntegrationErrorKind Kind { get; }

    public string Integration { get; }
}
