using System.Text.Json;
using System.Text.RegularExpressions;
using McpHub.Core.Errors;

namespace McpHub.Modules.Mongo;

/// <summary>
/// Validates model-written MongoDB documents, updates and filters before they are sent. Deny by default: only plain
/// data, a short list of update operators and comparison operators are accepted; nothing that can run JavaScript,
/// reach other collections or change _id. Restricted fields (passwords, card data, ...) can never be written.
/// </summary>
public static partial class MongoWriteGuard
{
    private const int MaxLength = 200_000;

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]{1,100}$")]
    private static partial Regex PathPattern();

    private static readonly HashSet<string> UpdateOperators = new(StringComparer.Ordinal)
    {
        "$set", "$unset", "$inc", "$mul", "$min", "$max", "$push", "$pull", "$addToSet"
    };

    /// <summary>Operators that may appear inside values: extended-JSON wrappers, $each and simple comparisons (for $pull).</summary>
    private static readonly HashSet<string> ValueOperators = new(StringComparer.Ordinal)
    {
        "$oid", "$date", "$numberLong", "$numberInt", "$numberDecimal", "$numberDouble", "$uuid", "$each",
        "$eq", "$ne", "$gt", "$gte", "$lt", "$lte", "$in", "$nin"
    };

    /// <summary>Accepts one JSON object or an array of objects; returns each document as JSON text.</summary>
    public static IReadOnlyList<string> ParseDocuments(string? json, DeniedColumnPolicy denied, int max)
    {
        using var document = Parse(json, "documents");
        var root = document.RootElement;
        var items = root.ValueKind switch
        {
            JsonValueKind.Object => [root],
            JsonValueKind.Array => root.EnumerateArray().ToList(),
            _ => throw Invalid("'documents' must be a JSON object or an array of objects.")
        };

        if (items.Count == 0)
        {
            throw Invalid("'documents' is empty.");
        }

        if (items.Count > max)
        {
            throw Invalid($"At most {max} documents can be inserted in one call.");
        }

        var result = new List<string>();
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("Every document must be a JSON object.");
            }

            WalkDocument(item, denied, topLevel: true);
            result.Add(item.GetRawText());
        }

        return result;
    }

    /// <summary>A write filter must select something: an empty filter would hit every document.</summary>
    public static string RequireFilter(string? filter, DeniedColumnPolicy denied)
    {
        var valid = MongoFilterGuard.Validate(filter, denied);
        using var document = JsonDocument.Parse(valid);
        if (!document.RootElement.EnumerateObject().Any())
        {
            throw Invalid("'filter' must not be empty: a write must say which documents it applies to.");
        }

        return valid;
    }

    /// <summary>Accepts {"$set": {...}, "$inc": {...}, ...} with only the listed operators and non-restricted fields.</summary>
    public static string ValidateUpdate(string? update, DeniedColumnPolicy denied)
    {
        using var document = Parse(update, "update");
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.EnumerateObject().Any())
        {
            throw Invalid("'update' must be a JSON object such as {\"$set\": {\"status\": \"done\"}}.");
        }

        foreach (var op in document.RootElement.EnumerateObject())
        {
            if (!UpdateOperators.Contains(op.Name))
            {
                throw Invalid($"The update operator '{op.Name}' is not allowed. Allowed: {string.Join(", ", UpdateOperators.Order(StringComparer.Ordinal))}.");
            }

            if (op.Value.ValueKind != JsonValueKind.Object || !op.Value.EnumerateObject().Any())
            {
                throw Invalid($"'{op.Name}' needs an object of field/value pairs.");
            }

            foreach (var field in op.Value.EnumerateObject())
            {
                CheckPath(field.Name, denied);
                if (field.Name.Split('.')[0] == "_id")
                {
                    throw Invalid("The _id field cannot be changed.");
                }

                WalkValue(field.Value, denied);
            }
        }

        return document.RootElement.GetRawText();
    }

    private static void WalkDocument(JsonElement element, DeniedColumnPolicy denied, bool topLevel)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.StartsWith('$'))
            {
                CheckOperator(property.Name);
            }
            else
            {
                if (property.Name.Contains('.'))
                {
                    throw Invalid($"Field names may not contain '.' ('{property.Name}').");
                }

                CheckPath(property.Name, denied);
            }

            WalkValue(property.Value, denied);
        }

        _ = topLevel;
    }

    private static void WalkValue(JsonElement value, DeniedColumnPolicy denied)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                WalkDocument(value, denied, topLevel: false);
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    WalkValue(item, denied);
                }

                break;
        }
    }

    private static void CheckOperator(string name)
    {
        if (!ValueOperators.Contains(name))
        {
            throw Invalid($"The operator '{name}' is not allowed inside a written value.");
        }
    }

    private static void CheckPath(string path, DeniedColumnPolicy denied)
    {
        if (!PathPattern().IsMatch(path))
        {
            throw Invalid($"'{path}' is not a valid field name.");
        }

        if (path.Split('.').Any(denied.IsDenied))
        {
            throw new IntegrationException(IntegrationErrorKind.Forbidden, MongoModule.IntegrationName,
                $"The field '{path}' is restricted and can never be written.");
        }
    }

    private static JsonDocument Parse(string? json, string what)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Invalid($"'{what}' is required.");
        }

        if (json.Length > MaxLength)
        {
            throw Invalid($"'{what}' is longer than {MaxLength} characters.");
        }

        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        }
        catch (JsonException)
        {
            throw Invalid($"'{what}' must be valid JSON (use {{\"$oid\": \"...\"}} for ids and {{\"$date\": \"...\"}} for dates).");
        }
    }

    private static IntegrationException Invalid(string message) =>
        new(IntegrationErrorKind.InvalidInput, MongoModule.IntegrationName, message);
}
