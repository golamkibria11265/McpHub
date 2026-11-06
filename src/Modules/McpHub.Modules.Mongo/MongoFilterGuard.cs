using System.Text.Json;
using System.Text.RegularExpressions;
using McpHub.Core.Errors;

namespace McpHub.Modules.Mongo;

/// <summary>
/// Validates a model-written MongoDB filter. Only a fixed list of query operators is accepted (deny by default),
/// which excludes everything that can run JavaScript ($where, $function, $accumulator) or touch other collections.
/// </summary>
public static partial class MongoFilterGuard
{
    private const int MaxLength = 5_000;

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]{1,120}$")]
    private static partial Regex CollectionPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]{1,100}$")]
    private static partial Regex FieldPattern();

    private static readonly HashSet<string> AllowedOperators = new(StringComparer.Ordinal)
    {
        "$and", "$or", "$nor", "$not", "$eq", "$ne", "$gt", "$gte", "$lt", "$lte", "$in", "$nin", "$exists", "$type",
        "$regex", "$options", "$all", "$elemMatch", "$size", "$mod",
        // Extended-JSON value wrappers, needed to express ids and dates.
        "$oid", "$date", "$numberLong", "$numberInt", "$numberDecimal", "$numberDouble", "$uuid"
    };

    /// <summary>Returns the filter to run ("{}" when none was given) or throws when it is not acceptable.</summary>
    public static string Validate(string? filterJson, DeniedColumnPolicy denied)
    {
        if (string.IsNullOrWhiteSpace(filterJson))
        {
            return "{}";
        }

        if (filterJson.Length > MaxLength)
        {
            throw Invalid($"'filter' is longer than {MaxLength} characters.");
        }

        try
        {
            using var document = JsonDocument.Parse(filterJson, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("'filter' must be a JSON object, e.g. {\"status\": \"failed\"}.");
            }

            Walk(document.RootElement, denied);
        }
        catch (JsonException)
        {
            throw Invalid("'filter' must be valid JSON (use {\"$oid\": \"...\"} for ids and {\"$date\": \"...\"} for dates).");
        }

        return filterJson.Trim();
    }

    public static string RequireCollection(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !CollectionPattern().IsMatch(name)
            || name.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("'collection' is not a valid collection name.");
        }

        return name;
    }

    public static IReadOnlyList<string> ValidateFields(IReadOnlyList<string>? fields, DeniedColumnPolicy denied)
    {
        if (fields is null || fields.Count == 0)
        {
            return [];
        }

        if (fields.Count > 50)
        {
            throw Invalid("At most 50 fields can be requested.");
        }

        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field) || !FieldPattern().IsMatch(field))
            {
                throw Invalid($"'{field}' is not a valid field name.");
            }

            if (field.Split('.').Any(denied.IsDenied))
            {
                throw Restricted(field);
            }
        }

        return fields;
    }

    private static void Walk(JsonElement element, DeniedColumnPolicy denied)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CheckKey(property.Name, denied);
                    Walk(property.Value, denied);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, denied);
                }

                break;
        }
    }

    private static void CheckKey(string key, DeniedColumnPolicy denied)
    {
        if (key.StartsWith('$'))
        {
            if (!AllowedOperators.Contains(key))
            {
                throw Invalid($"The operator '{key}' is not allowed.");
            }

            return;
        }

        if (key.Split('.').Any(denied.IsDenied))
        {
            throw Restricted(key);
        }
    }

    private static IntegrationException Restricted(string name) =>
        new(IntegrationErrorKind.Forbidden, MongoModule.IntegrationName,
            $"The field '{name}' is restricted and cannot be queried or returned.");

    private static IntegrationException Invalid(string message) =>
        new(IntegrationErrorKind.InvalidInput, MongoModule.IntegrationName, message);
}
