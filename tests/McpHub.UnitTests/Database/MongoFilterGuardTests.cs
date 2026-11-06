using McpHub.Core.Errors;
using System.Text.Json.Nodes;
using McpHub.Modules.Mongo;

namespace McpHub.UnitTests.Database;

public class MongoFilterGuardTests
{
    private static readonly DeniedColumnPolicy Policy = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"status":"failed","amount":{"$gt":100}}""")]
    [InlineData("""{"$or":[{"a":1},{"b":{"$in":[1,2,3]}}]}""")]
    [InlineData("""{"_id":{"$oid":"507f1f77bcf86cd799439011"}}""")]
    [InlineData("""{"createdAt":{"$gte":{"$date":"2026-01-01T00:00:00Z"}}}""")]
    public void Accepts_safe_filters(string? filter)
    {
        Assert.NotNull(MongoFilterGuard.Validate(filter, Policy));
    }

    [Theory]
    [InlineData("""{"$where":"sleep(1000)"}""")]
    [InlineData("""{"a":{"$function":{"body":"x","args":[],"lang":"js"}}}""")]
    [InlineData("""{"$expr":{"$eq":[1,1]}}""")]
    [InlineData("""{"a":{"$lookup":{}}}""")]
    [InlineData("[1,2]")]
    [InlineData("not json")]
    [InlineData("""{"a":""")]
    public void Rejects_unsafe_or_malformed_filters(string filter)
    {
        var ex = Assert.Throws<IntegrationException>(() => MongoFilterGuard.Validate(filter, Policy));

        Assert.Equal(IntegrationErrorKind.InvalidInput, ex.Kind);
    }

    [Theory]
    [InlineData("""{"card_number":"4111"}""")]
    [InlineData("""{"payment.cvv":"123"}""")]
    [InlineData("""{"$or":[{"password":"x"}]}""")]
    public void Rejects_filters_on_restricted_fields(string filter)
    {
        var ex = Assert.Throws<IntegrationException>(() => MongoFilterGuard.Validate(filter, Policy));

        Assert.Equal(IntegrationErrorKind.Forbidden, ex.Kind);
    }

    [Theory]
    [InlineData("orders", true)]
    [InlineData("payment-events_2026", true)]
    [InlineData("system.users", false)]
    [InlineData("a$b", false)]
    [InlineData("", false)]
    public void Collection_names_are_validated(string name, bool valid)
    {
        if (valid)
        {
            Assert.Equal(name, MongoFilterGuard.RequireCollection(name));
        }
        else
        {
            Assert.Throws<IntegrationException>(() => MongoFilterGuard.RequireCollection(name));
        }
    }

    [Fact]
    public void Restricted_fields_cannot_be_requested_in_projection()
    {
        var ex = Assert.Throws<IntegrationException>(() => MongoFilterGuard.ValidateFields(["id", "card.cvv"], Policy));

        Assert.Equal(IntegrationErrorKind.Forbidden, ex.Kind);
    }

    [Fact]
    public void Redactor_masks_nested_and_array_fields_and_counts_them()
    {
        var node = JsonNode.Parse("""{"id":1,"card":{"cvv":"123","brand":"visa"},"items":[{"token":"abc"},{"ok":true}]}""");

        var count = DocumentRedactor.Redact(node, Policy);

        Assert.Equal(2, count);
        Assert.Equal(DeniedColumnPolicy.Mask, (string?)node!["card"]!["cvv"]);
        Assert.Equal("visa", (string?)node["card"]!["brand"]);
        Assert.Equal(DeniedColumnPolicy.Mask, (string?)node["items"]![0]!["token"]);
    }
}
