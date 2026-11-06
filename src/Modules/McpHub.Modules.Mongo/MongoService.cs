using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using McpHub.Core.Errors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace McpHub.Modules.Mongo;

/// <summary>
/// Use cases for MongoDB: list collections, sample the shape of a collection, run a read-only find.
/// Only Find is ever issued (no aggregation pipelines, no writes), so there is nothing to switch to read-only.
/// Registered as a singleton because <see cref="MongoClient"/> is designed to be shared.
/// </summary>
public sealed class MongoService
{
    private const string Integration = MongoModule.IntegrationName;
    private const int SampleSize = 20;

    private readonly MongoOptions _options;
    private readonly ConnectionRegistry _registry;
    private readonly DeniedColumnPolicy _denied;
    private readonly ILogger<MongoService> _logger;

    public MongoService(IOptions<MongoOptions> options, IConfiguration configuration, ILogger<MongoService> logger)
    {
        _options = options.Value;
        _registry = new ConnectionRegistry(_options, configuration);
        _denied = new DeniedColumnPolicy(_options.ExtraDeniedColumns);
        _logger = logger;
    }

    private readonly ConcurrentDictionary<string, MongoClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ConnectionInfo> ListConnections() => _registry.List();

    public Task<ObjectList> ListCollectionsAsync(string? connection, CancellationToken cancellationToken) =>
        RunAsync(connection, async target =>
        {
            var database = GetDatabase(target);
            using var cursor = await database.ListCollectionNamesAsync(cancellationToken: cancellationToken);
            var names = await cursor.ToListAsync(cancellationToken);

            var objects = names
                .Where(n => !n.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Take(_options.MaxRows)
                .Select(n => new ObjectInfo(null, n, "collection"))
                .ToList();

            return new ObjectList(target.Name, objects, names.Count > objects.Count);
        });

    /// <summary>MongoDB has no schema, so this reports the top-level fields seen in a small sample of documents.</summary>
    public Task<ObjectDescription> DescribeAsync(string? connection, string? collectionName, CancellationToken cancellationToken) =>
        RunAsync(connection, async target =>
        {
            var name = MongoFilterGuard.RequireCollection(collectionName);
            var collection = GetDatabase(target).GetCollection<BsonDocument>(name);

            var sample = await collection
                .Find(new BsonDocumentFilterDefinition<BsonDocument>(new BsonDocument()), new FindOptions { MaxTime = CommandTimeout() })
                .Limit(SampleSize)
                .ToListAsync(cancellationToken);

            if (sample.Count == 0)
            {
                throw new IntegrationException(IntegrationErrorKind.NotFound, Integration, $"Collection '{name}' is empty or does not exist.");
            }

            var seen = new SortedDictionary<string, (HashSet<string> Types, int Count)>(StringComparer.Ordinal);
            foreach (var document in sample)
            {
                foreach (var element in document.Elements)
                {
                    if (!seen.TryGetValue(element.Name, out var entry))
                    {
                        entry = (new HashSet<string>(), 0);
                    }

                    entry.Types.Add(element.Value.BsonType.ToString());
                    seen[element.Name] = (entry.Types, entry.Count + 1);
                }
            }

            var fields = seen
                .Select(f => new ColumnInfo(f.Key, string.Join("|", f.Value.Types), f.Value.Count < sample.Count, null, _denied.IsDenied(f.Key)))
                .ToList();

            return new ObjectDescription(target.Name, null, name, fields);
        });

    public Task<DocumentResult> FindAsync(
        string? connection,
        string? collectionName,
        string? filter,
        IReadOnlyList<string>? fields,
        int? limit,
        CancellationToken cancellationToken) =>
        RunAsync(connection, async target =>
        {
            var name = MongoFilterGuard.RequireCollection(collectionName);
            var filterJson = MongoFilterGuard.Validate(filter, _denied);
            var projectionFields = MongoFilterGuard.ValidateFields(fields, _denied);
            var max = _options.ClampRows(limit);

            var collection = GetDatabase(target).GetCollection<BsonDocument>(name);
            var query = collection
                .Find(new BsonDocumentFilterDefinition<BsonDocument>(BsonDocument.Parse(filterJson)), new FindOptions { MaxTime = CommandTimeout() })
                .Limit(max + 1);

            if (projectionFields.Count > 0)
            {
                var projection = new BsonDocument(projectionFields.Select(f => new BsonElement(f, 1)));
                query = query.Project(new BsonDocumentProjectionDefinition<BsonDocument, BsonDocument>(projection));
            }

            var found = await query.ToListAsync(cancellationToken);
            var truncated = found.Count > max;

            var documents = new List<JsonNode?>();
            var redactedFields = 0;
            foreach (var document in found.Take(max))
            {
                var node = JsonNode.Parse(document.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson }));
                redactedFields += DocumentRedactor.Redact(node, _denied);
                documents.Add(LimitSize(node));
            }

            return new DocumentResult(target.Name, name, documents, documents.Count, truncated, redactedFields);
        });

    private JsonNode? LimitSize(JsonNode? node)
    {
        var json = node?.ToJsonString() ?? string.Empty;
        var limit = _options.MaxDocumentChars;

        return json.Length <= limit
            ? node
            : new JsonObject { ["_truncated"] = true, ["_preview"] = json[..limit] };
    }

    private TimeSpan CommandTimeout() => TimeSpan.FromSeconds(_options.CommandTimeoutSeconds);

    private IMongoDatabase GetDatabase(ResolvedConnection target)
    {
        MongoClient client;
        string? databaseName;

        try
        {
            client = _clients.GetOrAdd(target.Name, _ => new MongoClient(MongoClientSettings.FromConnectionString(target.ConnectionString)));
            databaseName = target.Database ?? MongoUrl.Create(target.ConnectionString).DatabaseName;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or MongoConfigurationException)
        {
            // Deliberately no inner exception: its message can quote parts of the connection string.
            throw new IntegrationException(IntegrationErrorKind.Unavailable, Integration,
                $"The connection string configured for '{target.Name}' is invalid. Contact the administrator.");
        }

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new IntegrationException(IntegrationErrorKind.Unavailable, Integration,
                $"No MongoDB database name is configured for '{target.Name}'. Set Modules:Mongo:Connections:{target.Name}:Database.");
        }

        return client.GetDatabase(databaseName);
    }

    private async Task<T> RunAsync<T>(string? connection, Func<ResolvedConnection, Task<T>> action)
    {
        var target = _registry.Resolve(connection);

        try
        {
            return await action(target);
        }
        catch (MongoException ex)
        {
            _logger.LogWarning(ex, "MongoDB request failed on connection {Connection}", target.Name);
            throw new IntegrationException(IntegrationErrorKind.Unavailable, Integration,
                $"The MongoDB request on '{target.Name}' failed or timed out.", ex);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "MongoDB request timed out on connection {Connection}", target.Name);
            throw new IntegrationException(IntegrationErrorKind.Unavailable, Integration,
                $"The MongoDB request on '{target.Name}' timed out.", ex);
        }
    }
}
