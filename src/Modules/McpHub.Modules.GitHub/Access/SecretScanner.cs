using System.Text.RegularExpressions;
using McpHub.Core.Errors;

namespace McpHub.Modules.GitHub.Access;

/// <summary>
/// Last check before text leaves this server for a repository: private keys, well-known token formats and
/// connection strings with an inline password are refused. Commit history is permanent, so a leaked secret must be
/// rotated; it is much better not to write it. Use [PLACEHOLDER] values instead.
/// </summary>
public static partial class SecretScanner
{
    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKeyPattern();

    [GeneratedRegex(@"\b(ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}")]
    private static partial Regex GitHubTokenPattern();

    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b|\bxox[abprs]-[A-Za-z0-9-]{10,}|\bAIza[0-9A-Za-z_-]{35}\b|\bPMAK-[A-Za-z0-9-]{30,}")]
    private static partial Regex ProviderTokenPattern();

    [GeneratedRegex(@"(?i)\b(password|pwd)\s*=\s*(?![\[{<$%@])[^;\s""']{6,}")]
    private static partial Regex ConnectionPasswordPattern();

    public static void EnsureClean(string? text, string field)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var found =
            PrivateKeyPattern().IsMatch(text) ? "a private key"
            : GitHubTokenPattern().IsMatch(text) || ProviderTokenPattern().IsMatch(text) ? "an access token or API key"
            : ConnectionPasswordPattern().IsMatch(text) ? "a connection string with a password"
            : null;

        if (found is not null)
        {
            throw new IntegrationException(IntegrationErrorKind.InvalidInput, GitHubModule.IntegrationName,
                $"'{field}' appears to contain {found}. Secrets are never written to a repository; use a [PLACEHOLDER] instead.");
        }
    }
}
