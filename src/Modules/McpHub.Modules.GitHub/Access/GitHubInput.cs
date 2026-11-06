using System.Text.RegularExpressions;
using McpHub.Core.Errors;

namespace McpHub.Modules.GitHub.Access;

/// <summary>
/// Strict validation of everything that ends up inside a GitHub URL or search query.
/// Values come from an LLM (and therefore, indirectly, from untrusted text), so they are allow-listed by shape.
/// </summary>
public static partial class GitHubInput
{
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9._/-]{1,200}$")]
    private static partial Regex RefPattern();

    // Search qualifiers that would widen the search beyond the repository we scoped it to.
    [GeneratedRegex(@"(^|[\s(-])(repo|org|user|owner)\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex ScopeQualifierPattern();

    public static string RequireName(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || !NamePattern().IsMatch(value) || value is "." or "..")
        {
            throw Invalid($"'{field}' is not a valid GitHub name.");
        }

        return value;
    }

    /// <summary>Validates a branch, tag or commit reference.</summary>
    public static string RequireRef(string? value, string field = "ref")
    {
        if (string.IsNullOrWhiteSpace(value) || !RefPattern().IsMatch(value) || value.Contains("..", StringComparison.Ordinal))
        {
            throw Invalid($"'{field}' is not a valid branch, tag or commit.");
        }

        return value;
    }

    public static string? OptionalRef(string? value, string field = "ref") =>
        string.IsNullOrWhiteSpace(value) ? null : RequireRef(value, field);

    public static string RequireSearchText(string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 256)
        {
            throw Invalid("'query' is required and must be at most 256 characters.");
        }

        if (ScopeQualifierPattern().IsMatch(query))
        {
            throw Invalid("The 'repo:', 'org:', 'user:' and 'owner:' qualifiers are not allowed; the search is already scoped to the repository.");
        }

        return query.Trim();
    }

    /// <summary>Maps open/closed/all to a GitHub search qualifier.</summary>
    public static string ResolveState(string? state) => state?.Trim().ToLowerInvariant() switch
    {
        null or "" or "all" => string.Empty,
        "open" => "is:open",
        "closed" => "is:closed",
        _ => throw Invalid("'state' must be 'open', 'closed' or 'all'.")
    };

    private static IntegrationException Invalid(string message) =>
        new(IntegrationErrorKind.InvalidInput, GitHubModule.IntegrationName, message);
}
