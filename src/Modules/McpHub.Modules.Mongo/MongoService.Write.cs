using McpHub.Core.Errors;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace McpHub.Modules.Mongo;

public sealed partial class MongoService
{
    public Task<WriteResult> InsertAsync(
        string? connection, string? collectionName, string? documents, bool confirm, CancellationToken cancellationToken) =>
        RunAsync(connection, async target =>
        {
            RequireWriteAllowed(target.Name, delete: false);
            var name = MongoFilterGuard.RequireCollection(collectionName);
            var docs = MongoWriteGuard.ParseDocuments(documents, _denied, _options.MaxAffectedRows);

            if (!confirm)
            {
                return new WriteResult(target.Name, "insert", docs.Count, false,
                    $"Preview only: {docs.Count} document(s) are valid and would be inserted. Nothing was changed. Run again with confirm=true to apply.");
            }

            var parsed = docs.Select(ParseBson).ToList();
            await GetDatabase(target).GetCollection<BsonDocument>(name).InsertManyAsync(parsed, cancellationToken: cancellationToken);
            _logger.LogInformation("Write on connection {Connection}: inserted {Count} document(s)", target.Name, parsed.Count);
            return new WriteResult(target.Name, "insert", parsed.Count, true, $"{parsed.Count} document(s) inserted.");
        });

    public Task<WriteResult> UpdateAsync(
        string? connection, string? collectionName, string? filter, string? update, bool many, bool confirm, CancellationToken cancellationToken) =>
        RunAsync(connection, async target =>
        {
            RequireWriteAllowed(target.Name, delete: false);
            var name = MongoFilterGuard.RequireCollection(collectionName);
            var filterJson = MongoWriteGuard.RequireFilter(filter, _denied);
            var updateJson = MongoWriteGuard.ValidateUpdate(update, _denied);

            var collection = GetDatabase(target).GetCollection<BsonDocument>(name);
            var filterDefinition = new BsonDocumentFilterDefinition<BsonDocument>(ParseBson(filterJson));
            var matched = await CountMatchesAsync(collection, filterDefinition, many, cancellationToken);
            if (matched == 0)
            {
                return new WriteResult(target.Name, "update", 0, false, "No document matches the filter. Nothing was changed.");
            }

            if (!confirm)
            {
                return new WriteResult(target.Name, "update", matched, false,
                    $"Preview only: {matched} document(s) would be updated. Nothing was changed. Run again with confirm=true to apply.");
            }

            var updateDefinition = new BsonDocumentUpdateDefinition<BsonDocument>(ParseBson(updateJson));
            var modified = many
                ? (await collection.UpdateManyAsync(filterDefinition, updateDefinition, cancellationToken: cancellationToken)).ModifiedCount
                : (await collection.UpdateOneAsync(filterDefinition, updateDefinition, cancellationToken: cancellationToken)).ModifiedCount;

            _logger.LogInformation("Write on connection {Connection}: updated {Count} document(s)", target.Name, modified);
            return new WriteResult(target.Name, "update", modified, true, $"{modified} document(s) updated.");
        });

    public Task<WriteResult> DeleteAsync(
        string? connection, string? collectionName, string? filter, bool many, bool confirm, CancellationToken cancellationToken) =>
        RunAsync(connection, async target =>
        {
            RequireWriteAllowed(target.Name, delete: true);
            var name = MongoFilterGuard.RequireCollection(collectionName);
            var filterJson = MongoWriteGuard.RequireFilter(filter, _denied);

            var collection = GetDatabase(target).GetCollection<BsonDocument>(name);
            var filterDefinition = new BsonDocumentFilterDefinition<BsonDocument>(ParseBson(filterJson));
            var matched = await CountMatchesAsync(collection, filterDefinition, many, cancellationToken);
            if (matched == 0)
            {
                return new WriteResult(target.Name, "delete", 0, false, "No document matches the filter. Nothing was changed.");
            }

            if (!confirm)
            {
                return new WriteResult(target.Name, "delete", matched, false,
                    $"Preview only: {matched} document(s) would be deleted. Nothing was changed. Run again with confirm=true to apply.");
            }

            var deleted = many
                ? (await collection.DeleteManyAsync(filterDefinition, cancellationToken)).DeletedCount
                : (await collection.DeleteOneAsync(filterDefinition, cancellationToken)).DeletedCount;

            _logger.LogInformation("Write on connection {Connection}: deleted {Count} document(s)", target.Name, deleted);
            return new WriteResult(target.Name, "delete", deleted, true, $"{deleted} document(s) deleted.");
        });

    /// <summary>Counts what the filter matches (one at most when not <paramref name="many"/>) and refuses to go past the cap.</summary>
    private async Task<long> CountMatchesAsync(
        IMongoCollection<BsonDocument> collection, FilterDefinition<BsonDocument> filter, bool many, CancellationToken cancellationToken)
    {
        var cap = _options.MaxAffectedRows;
        var matched = await collection.CountDocumentsAsync(
            filter, new CountOptions { Limit = cap + 1, MaxTime = CommandTimeout() }, cancellationToken);

        if (many && matched > cap)
        {
            throw new IntegrationException(IntegrationErrorKind.InvalidInput, Integration,
                $"The filter matches more than {cap} documents, which is the limit. Nothing was changed. Narrow the filter.");
        }

        return many ? matched : Math.Min(matched, 1);
    }

    private void RequireWriteAllowed(string connectionName, bool delete)
    {
        var settings = _options.Connections.First(c => string.Equals(c.Key, connectionName, StringComparison.OrdinalIgnoreCase)).Value;
        if (!settings.AllowWrite)
        {
            throw new IntegrationException(IntegrationErrorKind.Forbidden, Integration, "Writes are not enabled for this connection.");
        }

        if (delete && !settings.AllowDelete)
        {
            throw new IntegrationException(IntegrationErrorKind.Forbidden, Integration, "Deletes are not enabled for this connection.");
        }
    }

    private static BsonDocument ParseBson(string json)
    {
        try
        {
            return BsonDocument.Parse(json);
        }
        catch (Exception ex) when (ex is FormatException or BsonException)
        {
            throw new IntegrationException(IntegrationErrorKind.InvalidInput, Integration,
                "A value is not valid extended JSON (check {\"$oid\": ...} and {\"$date\": ...} values).");
        }
    }
}
