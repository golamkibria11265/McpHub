using McpHub.Core.Errors;

namespace McpHub.Modules.GitHub.Access;

/// <summary>
/// Refuses to read files that commonly hold secrets (keys, env files, state files). Anything sent to a tool
/// result is sent on to the model provider, so secrets that slipped into a repository must not travel further.
/// </summary>
public sealed class SensitivePathPolicy
{
    private static readonly string[] BlockedExtensions = [".pem", ".key", ".pfx", ".p12", ".jks", ".tfstate", ".kdbx"];

    private static readonly string[] BlockedNamePrefixes = [".env", "id_rsa", "id_ed25519", "id_ecdsa", "secrets", "credentials"];

    /// <summary>Validates and normalizes a repository-relative file path.</summary>
    public string EnsureReadable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 500 || path.Contains('\\') || path.Contains('\0'))
        {
            throw Invalid("'path' is required and must be a repository-relative path using '/'.");
        }

        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".."))
        {
            throw Invalid("'path' must not contain '.' or '..' segments.");
        }

        var fileName = segments[^1];
        var blocked =
            BlockedExtensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) ||
            BlockedNamePrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (blocked)
        {
            throw new IntegrationException(
                IntegrationErrorKind.Forbidden,
                GitHubModule.IntegrationName,
                "Reading files that commonly contain secrets is blocked by policy.");
        }

        return string.Join('/', segments);
    }

    private static IntegrationException Invalid(string message) =>
        new(IntegrationErrorKind.InvalidInput, GitHubModule.IntegrationName, message);
}
