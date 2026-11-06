using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace McpHub.Modules.Postman;

/// <summary>
/// One place that decides what counts as a secret in Postman data (auth headers, tokens, passwords, secret-type
/// variables) and masks it on the way out. It also stops the model from WRITING secrets or from writing back a
/// masked placeholder, which would overwrite the real value.
/// </summary>
public sealed partial class SecretPolicy
{
    public const string Mask = "[REDACTED]";

    private static readonly string[] Fragments =
    [
        "password", "passwd", "secret", "token", "apikey", "authorization", "privatekey", "bearer", "credential",
        "cardnumber", "cvv", "cvc", "sessionid", "cookie"
    ];

    private static readonly HashSet<string> Exact = new(StringComparer.Ordinal) { "pan", "pin", "otp", "pwd", "sig", "signature" };

    private readonly HashSet<string> _extra;

    public SecretPolicy(IEnumerable<string>? extra = null) =>
        _extra = [.. (extra ?? []).Select(Normalize).Where(n => n.Length > 0)];

    [GeneratedRegex(@"^\s*\{\{[^{}]+\}\}\s*$")]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"([?&])([^=&#]*)=([^&#]*)")]
    private static partial Regex QueryPairPattern();

    public bool IsSensitiveName(string? name)
    {
        var normalized = Normalize(name);
        return normalized.Length > 0
               && (Exact.Contains(normalized) || _extra.Contains(normalized) || Fragments.Any(f => normalized.Contains(f, StringComparison.Ordinal)));
    }

    public bool IsSensitiveVariable(string? key, string? type) =>
        string.Equals(type, "secret", StringComparison.OrdinalIgnoreCase) || IsSensitiveName(key);

    /// <summary>A value that is empty or only a {{variable}} reference carries no secret.</summary>
    public static bool IsHarmless(string? value) =>
        string.IsNullOrWhiteSpace(value) || ReferencePattern().IsMatch(value);

    /// <summary>Masks secret query parameters of a URL string.</summary>
    public string MaskUrl(string url) =>
        QueryPairPattern().Replace(url, m =>
            IsSensitiveName(m.Groups[2].Value) && !IsHarmless(m.Groups[3].Value)
                ? $"{m.Groups[1].Value}{m.Groups[2].Value}={Mask}"
                : m.Value);

    /// <summary>Masks secrets inside a JSON tree in place and returns how many values were masked.</summary>
    public int Redact(JsonNode? node)
    {
        var count = 0;

        switch (node)
        {
            case JsonObject obj:
                var pair = IsSecretPair(obj);
                foreach (var property in obj.ToList())
                {
                    if (property.Value is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        if ((pair && property.Key == "value") || IsSensitiveName(property.Key))
                        {
                            if (!IsHarmless(text))
                            {
                                obj[property.Key] = JsonValue.Create(Mask);
                                count++;
                            }
                        }
                        else if (property.Key is "raw" or "url")
                        {
                            var masked = MaskUrl(text);
                            if (masked != text)
                            {
                                obj[property.Key] = JsonValue.Create(masked);
                                count++;
                            }
                        }
                    }
                    else
                    {
                        count += Redact(property.Value);
                    }
                }

                break;
            case JsonArray array:
                foreach (var element in array)
                {
                    count += Redact(element);
                }

                break;
        }

        return count;
    }

    /// <summary>
    /// Returns the name of the first secret found in a payload that is about to be WRITTEN (or the mask placeholder),
    /// or null when the payload is clean. Secrets must be referenced as {{variables}}, never typed in.
    /// </summary>
    public string? FindSecret(JsonNode? node)
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var text) && text.Contains(Mask, StringComparison.Ordinal):
                return Mask;
            case JsonObject obj:
                var pair = IsSecretPair(obj);
                foreach (var property in obj)
                {
                    if (property.Value is JsonValue v && v.TryGetValue<string>(out var s))
                    {
                        if (s.Contains(Mask, StringComparison.Ordinal))
                        {
                            return Mask;
                        }

                        if (((pair && property.Key == "value") || IsSensitiveName(property.Key)) && !IsHarmless(s))
                        {
                            return pair && property.Key == "value" ? SecretPairName(obj) : property.Key;
                        }

                        if (property.Key is "raw" or "url" && MaskUrl(s) != s)
                        {
                            return "url query";
                        }
                    }
                    else if (FindSecret(property.Value) is { } nested)
                    {
                        return nested;
                    }
                }

                return null;
            case JsonArray array:
                foreach (var element in array)
                {
                    if (FindSecret(element) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    private bool IsSecretPair(JsonObject obj) =>
        (obj["key"] is JsonValue key && key.TryGetValue<string>(out var keyName) && IsSensitiveName(keyName))
        || (obj["type"] is JsonValue type && type.TryGetValue<string>(out var typeName)
            && string.Equals(typeName, "secret", StringComparison.OrdinalIgnoreCase));

    private static string SecretPairName(JsonObject obj) =>
        obj["key"] is JsonValue key && key.TryGetValue<string>(out var name) ? name : "secret variable";

    private static string Normalize(string? name) =>
        name is null ? string.Empty : new string([.. name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);
}
