using McpHub.Core.Errors;
using McpHub.Modules.GitHub.Access;

namespace McpHub.UnitTests.GitHub;

public class GitHubOwnerTests
{
    [Fact]
    public void Configured_owner_comes_from_settings()
    {
        var policy = new RepositoryAccessPolicy(TestOptions.GitHub("acme-corp"));

        Assert.Equal("acme-corp", policy.ConfiguredOwner);
    }

    [Fact]
    public void Configured_owner_is_unavailable_when_not_exactly_one()
    {
        var none = new RepositoryAccessPolicy(TestOptions.GitHub());
        var several = new RepositoryAccessPolicy(TestOptions.GitHub("a", "b"));

        Assert.Equal(IntegrationErrorKind.Unavailable, Assert.Throws<IntegrationException>(() => _ = none.ConfiguredOwner).Kind);
        Assert.Equal(IntegrationErrorKind.Unavailable, Assert.Throws<IntegrationException>(() => _ = several.ConfiguredOwner).Kind);
    }
}
