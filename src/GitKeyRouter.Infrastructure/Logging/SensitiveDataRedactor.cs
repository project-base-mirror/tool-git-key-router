using System.Text.RegularExpressions;

namespace GitKeyRouter.Infrastructure.Logging;

public static partial class SensitiveDataRedactor
{
    private const string RedactedPrivateKey = "[REDACTED OPENSSH PRIVATE KEY]";

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        return PrivateKeyPattern().Replace(value, RedactedPrivateKey);
    }

    [GeneratedRegex("-----BEGIN (?:OPENSSH|RSA|EC|DSA) PRIVATE KEY-----.*?-----END (?:OPENSSH|RSA|EC|DSA) PRIVATE KEY-----", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKeyPattern();
}
