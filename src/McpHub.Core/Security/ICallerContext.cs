namespace McpHub.Core.Security;

/// <summary>Who is calling the MCP server, and what are they allowed to do.</summary>
public interface ICallerContext
{
    /// <summary>Stable identifier of the caller (token subject). Safe to log.</summary>
    string UserId { get; }

    bool HasScope(string scope);
}
