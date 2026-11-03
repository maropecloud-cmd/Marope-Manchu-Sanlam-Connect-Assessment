using IHub.Api.Domain;
using IHub.Api.Features.Activities;
using IHub.Api.Tests.Support;
using Microsoft.Extensions.Options;

namespace IHub.Api.Tests.Unit;

public sealed class ActivitySubmissionValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly ActivitySubmissionValidator Validator = new(
        Options.Create(new IngestionOptions
        {
            MaxBatchSize = 3,
            MaxClockSkew = TimeSpan.FromMinutes(5),
            SupportedCurrencies = ["ZAR", "USD"],
        }),
        new FixedTimeProvider(Now));

    private static (bool IsValid, IReadOnlyList<ActivityRevision> Revisions, Dictionary<string, string[]> Errors) Validate(
        params ActivitySubmission[] activities)
    {
        var isValid = Validator.TryValidate(new SubmitActivitiesRequest(activities), out var revisions, out var errors);
        return (isValid, revisions, errors);
    }

    [Fact]
    public void Valid_activity_is_converted_to_a_normalised_revision()
    {
        var (isValid, revisions, _) = Validate(
            TestActivities.Build("BRK-1", "A-1", status: "reversed", currency: "zar", occurredAt: "2026-11-01T10:00:00+02:00"));

        Assert.True(isValid);
        var revision = Assert.Single(revisions);
        Assert.Equal(ActivityStatus.Reversed, revision.Status);
        Assert.Equal("ZAR", revision.Currency);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero), revision.OccurredAt);
    }

    [Fact]
    public void Missing_fields_are_reported_individually_not_defaulted()
    {
        var (isValid, _, errors) = Validate(new ActivitySubmission(null, null, null, null, null, null, null, null, null));

        Assert.False(isValid);
        Assert.Equivalent(
            new[]
            {
                "activities[0].sourceSystem", "activities[0].sourceActivityId", "activities[0].intermediaryId",
                "activities[0].activityType", "activities[0].version", "activities[0].status", "activities[0].amount",
                "activities[0].currency", "activities[0].occurredAt",
            },
            errors.Keys);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.23456)]
    public void Negative_or_over_precise_amounts_are_rejected(double amount)
    {
        var (isValid, _, errors) = Validate(TestActivities.Build("BRK-1", amount: (decimal)amount));

        Assert.False(isValid);
        Assert.Contains("activities[0].amount", errors.Keys);
    }

    [Fact]
    public void Timestamp_without_offset_is_rejected()
    {
        var (isValid, _, errors) = Validate(TestActivities.Build("BRK-1", occurredAt: "2026-11-01T08:00:00"));

        Assert.False(isValid);
        Assert.Contains("activities[0].occurredAt", errors.Keys);
    }

    [Fact]
    public void Future_timestamp_beyond_clock_skew_is_rejected_but_within_skew_is_accepted()
    {
        var withinSkew = Validate(TestActivities.Build("BRK-1", occurredAt: "2026-11-02T12:04:00Z"));
        var beyondSkew = Validate(TestActivities.Build("BRK-1", occurredAt: "2026-11-02T12:06:00Z"));

        Assert.True(withinSkew.IsValid);
        Assert.False(beyondSkew.IsValid);
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("RAND")]
    public void Unsupported_currency_is_rejected(string currency)
    {
        Assert.False(Validate(TestActivities.Build("BRK-1", currency: currency)).IsValid);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("1")]
    public void Unknown_status_is_rejected(string status)
    {
        Assert.False(Validate(TestActivities.Build("BRK-1", status: status)).IsValid);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("naïve")]
    public void Non_ascii_or_whitespace_identifiers_are_rejected(string sourceActivityId)
    {
        Assert.False(Validate(TestActivities.Build("BRK-1", sourceActivityId)).IsValid);
    }

    [Fact]
    public void Version_must_be_positive()
    {
        Assert.False(Validate(TestActivities.Build("BRK-1", version: 0)).IsValid);
    }

    [Fact]
    public void Batch_is_rejected_as_a_whole_when_any_item_is_invalid()
    {
        var (isValid, revisions, errors) = Validate(
            TestActivities.Build("BRK-1"),
            TestActivities.Build("BRK-1", amount: -1));

        Assert.False(isValid);
        Assert.Empty(revisions);
        Assert.Contains("activities[1].amount", errors.Keys);
    }

    [Fact]
    public void Empty_and_oversized_batches_are_rejected()
    {
        Assert.False(Validate().IsValid);
        Assert.False(Validate(
            TestActivities.Build("BRK-1"), TestActivities.Build("BRK-1"),
            TestActivities.Build("BRK-1"), TestActivities.Build("BRK-1")).IsValid);
    }
}
