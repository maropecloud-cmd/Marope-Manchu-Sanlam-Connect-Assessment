using System.Globalization;
using System.Text.RegularExpressions;

namespace IHub.Api.Common;

// The offset is mandatory: without one the server's local zone would be assumed.
internal static partial class UtcTimestamp
{
    public const string FormatHint =
        "an ISO-8601 timestamp with an explicit offset, e.g. 2026-11-01T08:30:00Z or 2026-11-01T10:30:00+02:00";

    public static bool TryParse(string? value, out DateTimeOffset utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(value) || !Iso8601WithOffset().IsMatch(value))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return false;
        }

        utc = parsed.ToUniversalTime();
        return true;
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex Iso8601WithOffset();
}
