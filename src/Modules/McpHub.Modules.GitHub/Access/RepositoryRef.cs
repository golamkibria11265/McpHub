namespace McpHub.Modules.GitHub.Access;

/// <summary>A validated owner/name pair. Can only be created through <see cref="Create"/>, so holding one proves the input was checked.</summary>
public sealed record RepositoryRef
{
    private RepositoryRef(string owner, string name)
    {
        Owner = owner;
        Name = name;
    }

    public string Owner { get; }

    public string Name { get; }

    /// <summary>Relative API path, safely escaped: <c>repos/{owner}/{name}</c>.</summary>
    public string ApiPath => $"repos/{Uri.EscapeDataString(Owner)}/{Uri.EscapeDataString(Name)}";

    public static RepositoryRef Create(string? owner, string? name) =>
        new(GitHubInput.RequireName(owner, "owner"), GitHubInput.RequireName(name, "repo"));

    public override string ToString() => $"{Owner}/{Name}";
}
