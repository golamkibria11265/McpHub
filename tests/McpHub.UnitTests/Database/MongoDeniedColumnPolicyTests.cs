using McpHub.Modules.Mongo;

namespace McpHub.UnitTests.Database;

public class MongoDeniedColumnPolicyTests
{
    private static readonly DeniedColumnPolicy Policy = new(["ssn"]);

    [Theory]
    [InlineData("card_number")]
    [InlineData("CardNumber")]
    [InlineData("CVV")]
    [InlineData("password-hash")]
    [InlineData("SSN")]
    public void Restricted_names_match_ignoring_case_and_separators(string name)
    {
        Assert.True(Policy.IsDenied(name));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("pan_count")]
    [InlineData("amount")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_names_are_not_restricted(string? name)
    {
        Assert.False(Policy.IsDenied(name));
    }
}
