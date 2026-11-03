using System.Text.RegularExpressions;

namespace IHub.Api.Common;

// ASCII only: varchar columns silently replace other characters, which could make two IDs collide.
internal static partial class Identifiers
{
    public static bool IsValidCode(string value) => CodePattern().IsMatch(value);

    public static bool IsValidExternalId(string value) => ExternalIdPattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^[\x21-\x7E]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ExternalIdPattern();
}
