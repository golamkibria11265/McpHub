using McpHub.Core.Errors;
using McpHub.Modules.Mongo;

namespace McpHub.UnitTests.Database;

public sealed class MongoWriteGuardTests
{
    private static readonly DeniedColumnPolicy Denied = new();

    [Fact]
    public void Insert_accepts_plain_documents_and_extended_json()
    {
        var docs = MongoWriteGuard.ParseDocuments("""[{"name":"a","when":{"$date":"2026-01-01T00:00:00Z"}},{"n":1}]""", Denied, 10);
        Assert.Equal(2, docs.Count);
        Assert.Equal(1, MongoWriteGuard.ParseDocuments("""{"name":"a"}""", Denied, 10).Count);
    }

    [Theory]
    [InlineData("""{"password":"x"}""")]
    [InlineData("""{"a":{"token":"x"}}""")]
    [InlineData("""{"$where":"1"}""")]
    [InlineData("""{"a":{"$function":{}}}""")]
    [InlineData("""{"a.b":1}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("5")]
    public void Insert_rejects_unsafe_documents(string json) =>
        Assert.Throws<IntegrationException>(() => MongoWriteGuard.ParseDocuments(json, Denied, 10));

    [Fact]
    public void Insert_enforces_the_document_cap() =>
        Assert.Throws<IntegrationException>(() => MongoWriteGuard.ParseDocuments("""[{"a":1},{"a":2},{"a":3}]""", Denied, 2));

    [Theory]
    [InlineData("{}")]
    [InlineData("")]
    [InlineData("""{"$where":"1"}""")]
    public void Write_filter_must_be_valid_and_not_empty(string filter) =>
        Assert.Throws<IntegrationException>(() => MongoWriteGuard.RequireFilter(filter, Denied));

    [Fact]
    public void Write_filter_accepts_a_real_condition() =>
        Assert.NotNull(MongoWriteGuard.RequireFilter("""{"status":"failed"}""", Denied));

    [Fact]
    public void Update_accepts_listed_operators() =>
        Assert.NotNull(MongoWriteGuard.ValidateUpdate("""{"$set":{"status":"done"},"$inc":{"tries":1},"$push":{"tags":{"$each":["a"]}}}""", Denied));

    [Theory]
    [InlineData("""{"status":"done"}""")]
    [InlineData("""{"$rename":{"a":"b"}}""")]
    [InlineData("""{"$set":{"_id":1}}""")]
    [InlineData("""{"$set":{"password":"x"}}""")]
    [InlineData("""{"$set":{"a":{"$function":1}}}""")]
    [InlineData("""{"$set":{}}""")]
    [InlineData("{}")]
    public void Update_rejects_unsafe_updates(string json) =>
        Assert.Throws<IntegrationException>(() => MongoWriteGuard.ValidateUpdate(json, Denied));
}
