using McpHub.Core.Errors;
using McpHub.Modules.GitHub.Access;

namespace McpHub.UnitTests.GitHub;

public class AccessPolicyTests
{
    private static RepositoryAccessPolicy Policy() => new(TestOptions.GitHub("acme"));

    [Fact]
    public void Resolve_allows_configured_owner_case_insensitively()
    {
        var repository = Policy().Resolve("ACME", "portal");

        Assert.Equal("portal", repository.Name);
    }

    [Fact]
    public void Resolve_rejects_owner_outside_allow_list()
    {
        var ex = Assert.Throws<IntegrationException>(() => Policy().Resolve("someone-else", "portal"));

        Assert.Equal(IntegrationErrorKind.Forbidden, ex.Kind);
    }

    [Theory]
    [InlineData("..", "x")]
    [InlineData("acme/x", "y")]
    [InlineData("acme", "")]
    [InlineData("acme", "a b")]
    [InlineData("acme", "x?y=1")]
    public void Resolve_rejects_malformed_names(string owner, string repo)
    {
        var ex = Assert.Throws<IntegrationException>(() => Policy().Resolve(owner, repo));

        Assert.Equal(IntegrationErrorKind.InvalidInput, ex.Kind);
    }

    [Theory]
    [InlineData(".env")]
    [InlineData("config/.env.production")]
    [InlineData("certs/server.pem")]
    [InlineData("infra/terraform.tfstate")]
    public void SensitivePathPolicy_blocks_secret_files(string path)
    {
        var ex = Assert.Throws<IntegrationException>(() => new SensitivePathPolicy().EnsureReadable(path));

        Assert.Equal(IntegrationErrorKind.Forbidden, ex.Kind);
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("a/../b")]
    [InlineData("")]
    public void SensitivePathPolicy_rejects_traversal_and_empty_paths(string path)
    {
        var ex = Assert.Throws<IntegrationException>(() => new SensitivePathPolicy().EnsureReadable(path));

        Assert.Equal(IntegrationErrorKind.InvalidInput, ex.Kind);
    }

    [Fact]
    public void SensitivePathPolicy_normalizes_valid_paths()
    {
        Assert.Equal("src/Program.cs", new SensitivePathPolicy().EnsureReadable("/src/Program.cs"));
    }
}
