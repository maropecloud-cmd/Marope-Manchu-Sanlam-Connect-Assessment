using System.ComponentModel.DataAnnotations;

namespace IHub.Api.Features.Statements;

public sealed class StatementOptions
{
    public const string SectionName = "Statements";

    [Range(typeof(TimeSpan), "1.00:00:00", "3660.00:00:00")]
    public TimeSpan MaxWindow { get; set; } = TimeSpan.FromDays(366);
}
