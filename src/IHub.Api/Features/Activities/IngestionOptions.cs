using System.ComponentModel.DataAnnotations;

namespace IHub.Api.Features.Activities;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    [Range(1, 5_000)]
    public int MaxBatchSize { get; set; } = 500;

    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan MaxClockSkew { get; set; } = TimeSpan.FromMinutes(5);

    [MinLength(1)]
    public string[] SupportedCurrencies { get; set; } = [];

    public string[] KnownSourceSystems { get; set; } = [];
}
