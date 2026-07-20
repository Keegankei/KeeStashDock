using System.Globalization;

namespace KeeStashDock.Infrastructure.Database;

/// <summary>Single persistence format for database timestamps.</summary>
public static class UtcTimestamp
{
    public const string InvalidRequiredFallback = "1970-01-01T00:00:00.0000000Z";

    private static readonly string[] LegacyFormats =
    [
        "O",
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm:ss",
    ];

    public static string Format(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Database timestamps must be explicitly converted to UTC.",
                nameof(value));
        }

        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    public static DateTime Parse(string value)
    {
        if (!TryNormalize(value, out string? normalized))
        {
            throw new FormatException($"Invalid persisted UTC timestamp: '{value}'.");
        }

        return DateTime.ParseExact(
            normalized!,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }

    public static bool TryNormalize(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!DateTimeOffset.TryParseExact(
                value,
                LegacyFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces |
                DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed))
        {
            return false;
        }

        normalized = Format(parsed.UtcDateTime);
        return true;
    }
}
