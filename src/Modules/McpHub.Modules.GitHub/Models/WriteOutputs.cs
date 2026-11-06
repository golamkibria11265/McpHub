namespace McpHub.Modules.GitHub.Models;

/// <summary>Outcome of a write: what happened, and where to look at it.</summary>
public sealed record WriteOutcome(string Action, string Repository, int? Number, string? Reference, string? Url);
