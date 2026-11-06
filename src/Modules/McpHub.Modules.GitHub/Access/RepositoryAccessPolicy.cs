using McpHub.Core.Errors;
using Microsoft.Extensions.Options;

namespace McpHub.Modules.GitHub.Access;

/// <summary>
/// The single gatekeeper for "which repositories may be touched". Every service resolves its target through here,
/// so the allow-list cannot be forgotten by a new tool.
/// </summary>
public sealed class RepositoryAccessPolicy(IOptions<GitHubOptions> options)
{
    public string ResolveOwner(string? owner)
    {
        var valid = GitHubInput.RequireName(owner, "owner");
        EnsureAllowed(valid);
        return valid;
    }

    public RepositoryRef Resolve(string? owner, string? repo)
    {
        var repository = RepositoryRef.Create(owner, repo);
        EnsureAllowed(repository.Owner);
        return repository;
    }

    private void EnsureAllowed(string owner)
    {
        if (!options.Value.AllowedOwners.Contains(owner, StringComparer.OrdinalIgnoreCase))
        {
            // Deliberately does not reveal which owners are allowed.
            throw new IntegrationException(
                IntegrationErrorKind.Forbidden,
                GitHubModule.IntegrationName,
                $"Access to '{owner}' is not permitted by this server's configuration.");
        }
    }
}
