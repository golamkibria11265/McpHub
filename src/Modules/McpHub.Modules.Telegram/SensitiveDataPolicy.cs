using System.Text.RegularExpressions;

namespace McpHub.Modules.Telegram;

/// <summary>
/// PCI-DSS guard rail. Chats can contain card numbers (customers pasting them to support, for example). Anything that
/// looks like a payment card number (13 to 19 digits that pass the Luhn check) is masked on the way out to the model and
/// refused on the way in, so this server is never a path for cardholder data in either direction.
/// </summary>
public static partial class SensitiveDataPolicy
{
    public const string Mask = "[REDACTED-CARD-NUMBER]";

    [GeneratedRegex(@"(?<![\d])(?:\d[ -]?){12,18}\d(?![\d])", RegexOptions.CultureInvariant)]
    private static partial Regex Candidate();

    public static bool ContainsCardNumber(string? text) =>
        !string.IsNullOrEmpty(text) && Candidate().Matches(text).Any(m => IsCardNumber(m.Value));

    public static string? Redact(string? text) =>
        string.IsNullOrEmpty(text)
            ? text
            : Candidate().Replace(text, m => IsCardNumber(m.Value) ? Mask : m.Value);

    private static bool IsCardNumber(string candidate)
    {
        Span<int> digits = stackalloc int[19];
        var count = 0;
        foreach (var c in candidate)
        {
            if (!char.IsAsciiDigit(c))
            {
                continue;
            }

            if (count == digits.Length)
            {
                return false;
            }

            digits[count++] = c - '0';
        }

        if (count is < 13 or > 19)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < count; i++)
        {
            var d = digits[count - 1 - i];
            if (i % 2 == 1)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
        }

        return sum % 10 == 0;
    }
}
