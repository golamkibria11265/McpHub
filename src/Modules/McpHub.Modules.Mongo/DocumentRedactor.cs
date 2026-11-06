using System.Text.Json.Nodes;

namespace McpHub.Modules.Mongo;

/// <summary>Masks restricted fields anywhere inside a document, including nested objects and arrays.</summary>
public static class DocumentRedactor
{
    /// <summary>Returns how many values were masked.</summary>
    public static int Redact(JsonNode? node, DeniedColumnPolicy denied)
    {
        var count = 0;

        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (denied.IsDenied(key))
                    {
                        obj[key] = JsonValue.Create(DeniedColumnPolicy.Mask);
                        count++;
                    }
                    else
                    {
                        count += Redact(obj[key], denied);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    count += Redact(item, denied);
                }

                break;
        }

        return count;
    }
}
