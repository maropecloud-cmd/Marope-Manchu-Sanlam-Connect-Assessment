using IHub.Api.Domain;

namespace IHub.Api.Tests.Unit;

public sealed class ActivityRevisionTests
{
    private static ActivityRevision Revision(decimal amount = 100m, string occurredAt = "2026-11-01T08:30:00Z", long version = 1) =>
        new("life-admin", "A-1", version, "BRK-1", "commission", ActivityStatus.Active, amount, "ZAR",
            DateTimeOffset.Parse(occurredAt, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Content_hash_ignores_amount_scale_and_time_zone_representation()
    {
        var a = Revision(amount: 100m, occurredAt: "2026-11-01T08:30:00Z");
        var b = Revision(amount: 100.0000m, occurredAt: "2026-11-01T10:30:00+02:00");

        Assert.Equal(a.ContentHash, b.ContentHash);
    }

    [Fact]
    public void Content_hash_ignores_version_so_redelivered_versions_compare_by_payload()
    {
        Assert.Equal(Revision(version: 1).ContentHash, Revision(version: 2).ContentHash);
    }

    [Fact]
    public void Content_hash_changes_when_business_content_changes()
    {
        Assert.NotEqual(Revision(amount: 100m).ContentHash, Revision(amount: 100.01m).ContentHash);
    }
}
