using System.Globalization;

namespace McpHub.Modules.Sql;

/// <summary>Turns database values into plain JSON-safe values (no binary blobs, no NaN, bounded text).</summary>
public static class ValueFormatter
{
    private const int MaxArrayItems = 100;

    public static object? Format(object? value, int maxChars) => value switch
    {
        null or DBNull => null,
        string s => Truncate(s, maxChars),
        bool or byte or sbyte or short or ushort or int or uint or long or ulong or decimal => value,
        float f => float.IsFinite(f) ? f : f.ToString(CultureInfo.InvariantCulture),
        double d => double.IsFinite(d) ? d : d.ToString(CultureInfo.InvariantCulture),
        byte[] bytes => $"[binary {bytes.Length} bytes]",
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        Array array => array.Cast<object?>().Take(MaxArrayItems).Select(item => Format(item, maxChars)).ToList(),
        _ => Truncate(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, maxChars)
    };

    public static string Truncate(string text, int maxChars) =>
        text.Length <= maxChars ? text : string.Concat(text.AsSpan(0, maxChars), "...[truncated]");
}
