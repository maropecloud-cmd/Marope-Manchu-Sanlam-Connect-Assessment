using IHub.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IHub.Api.Persistence;

internal sealed class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    // Case-sensitive: the default collation would treat "pol-1" and "POL-1" as one activity.
    private const string IdentifierCollation = "Latin1_General_100_BIN2";

    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.ToTable("Activities", table =>
        {
            table.HasCheckConstraint("CK_Activities_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_Activities_Amount", "[Amount] >= 0");
            table.HasCheckConstraint("CK_Activities_Status", "[Status] IN ('Active', 'Reversed')");
        });

        // Increasing clustered key: inserts append instead of splitting pages.
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityColumn();

        builder.Property(a => a.SourceSystem)
            .HasMaxLength(ActivityLimits.SourceSystemMaxLength).IsUnicode(false).UseCollation(IdentifierCollation);
        builder.Property(a => a.SourceActivityId)
            .HasMaxLength(ActivityLimits.SourceActivityIdMaxLength).IsUnicode(false).UseCollation(IdentifierCollation);
        builder.Property(a => a.IntermediaryId)
            .HasMaxLength(ActivityLimits.IntermediaryIdMaxLength).IsUnicode(false).UseCollation(IdentifierCollation);
        builder.Property(a => a.ActivityType)
            .HasMaxLength(ActivityLimits.ActivityTypeMaxLength).IsUnicode(false);
        builder.Property(a => a.Status)
            .HasConversion<string>().HasMaxLength(ActivityLimits.StatusMaxLength).IsUnicode(false);
        builder.Property(a => a.Amount)
            .HasPrecision(ActivityLimits.AmountPrecision, ActivityLimits.AmountScale);
        builder.Property(a => a.Currency)
            .HasMaxLength(ActivityLimits.CurrencyLength).IsFixedLength().IsUnicode(false);
        builder.Property(a => a.ContentHash)
            .HasMaxLength(ActivityLimits.ContentHashLength).IsFixedLength();
        builder.Property(a => a.LastCorrelationId)
            .HasMaxLength(ActivityLimits.CorrelationIdMaxLength).IsUnicode(false);

        builder.HasIndex(a => new { a.SourceSystem, a.SourceActivityId })
            .IsUnique()
            .HasDatabaseName("UX_Activities_SourceKey");

        builder.HasIndex(a => new { a.IntermediaryId, a.OccurredAt })
            .IncludeProperties(a => new { a.Status, a.Currency, a.Amount, a.UpdatedAt })
            .HasDatabaseName("IX_Activities_Intermediary_OccurredAt");
    }
}
