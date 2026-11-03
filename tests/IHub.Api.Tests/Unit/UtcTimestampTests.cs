using IHub.Api.Common;

namespace IHub.Api.Tests.Unit;

public sealed class UtcTimestampTests
{
    [Theory]
    [InlineData("2026-11-01T08:30:00Z", "2026-11-01T08:30:00+00:00")]
    [InlineData("2026-11-01T10:30:00+02:00", "2026-11-01T08:30:00+00:00")]
    [InlineData("2026-11-01T08:30Z", "2026-11-01T08:30:00+00:00")]
    [InlineData("2026-11-01T08:30:00.1234567Z", "2026-11-01T08:30:00.1234567+00:00")]
    public void Accepts_timestamps_with_explicit_offset_and_normalises_to_utc(string input, string expectedUtc)
    {
        Assert.True(UtcTimestamp.TryParse(input, out var utc));
        Assert.Equal(DateTimeOffset.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture), utc);
        Assert.Equal(TimeSpan.Zero, utc.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-11-01T08:30:00")] // no offset: ambiguous, must be rejected
    [InlineData("2026-11-01")]
    [InlineData("2026-11-01 08:30:00Z")]
    [InlineData("2026-11-01T08:30:00 02:00")] // '+' decoded to a space in a query string
    [InlineData("2026-13-01T08:30:00Z")]
    [InlineData("01/11/2026 08:30")]
    public void Rejects_ambiguous_or_malformed_timestamps(string? input)
    {
        Assert.False(UtcTimestamp.TryParse(input, out _));
    }
}
