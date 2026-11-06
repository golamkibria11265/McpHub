namespace McpHub.Modules.Mongo;

/// <summary>
/// Names of columns/fields that must never leave the database through this server: credentials, card data, PINs.
/// Two layers use it: queries that mention such a name are rejected, and any such column that still shows up in a
/// result (for example through SELECT *) is replaced by a mask. Matching ignores case, '_' and '-'.
/// </summary>
public sealed class DeniedColumnPolicy
{
    public const string Mask = "[REDACTED]";

    private static readonly string[] Defaults =
    [
        "password", "passwd", "pwd", "passwordhash", "passwordsalt",
        "secret", "clientsecret", "apikey", "apisecret", "privatekey", "mfasecret", "otp",
        "token", "accesstoken", "refreshtoken", "authtoken",
        "cardnumber", "cardno", "cardnum", "pan", "cvv", "cvv2", "cvc", "cardcvv", "pin", "pinblock", "track1", "track2"
    ];

    private readonly HashSet<string> _denied;

    public DeniedColumnPolicy(IEnumerable<string>? extra = null)
    {
        _denied = new HashSet<string>(Defaults.Concat(extra ?? []).Select(Normalize), StringComparer.Ordinal);
    }

    public bool IsDenied(string? name) => !string.IsNullOrEmpty(name) && _denied.Contains(Normalize(name));

    /// <summary>Returns the first identifier that is restricted, or null.</summary>
    public string? FindReference(IEnumerable<string> identifiers) => identifiers.FirstOrDefault(IsDenied);

    private static string Normalize(string name) =>
        new([.. name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);
}
