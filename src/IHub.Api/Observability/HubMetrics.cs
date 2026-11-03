using System.Diagnostics.Metrics;
using IHub.Api.Features.Activities;
using Microsoft.Extensions.Options;

namespace IHub.Api.Observability;

public sealed class HubMetrics
{
    public const string MeterName = "IHub.Api";
    private const string OtherSourceSystem = "other";

    // Seconds, up to a day: batch sources can deliver hours late.
    private static readonly double[] LagBuckets = [1, 5, 15, 30, 60, 120, 300, 600, 1_800, 3_600, 10_800, 21_600, 86_400];

    private readonly Counter<long> _activitiesProcessed;
    private readonly Counter<long> _batchesRejected;
    private readonly Counter<long> _errors;
    private readonly Histogram<double> _ingestLag;
    private readonly HashSet<string> _knownSourceSystems;

    public HubMetrics(IMeterFactory meterFactory, IOptions<IngestionOptions> options)
    {
        var meter = meterFactory.Create(MeterName);

        _activitiesProcessed = meter.CreateCounter<long>(
            "ihub.activities.processed",
            unit: "{activity}",
            description: "Activities processed by ingestion, by outcome (created, updated, duplicate, stale, conflict).");

        _batchesRejected = meter.CreateCounter<long>(
            "ihub.ingest.batches.rejected",
            unit: "{batch}",
            description: "Ingestion batches rejected before processing, by reason.");

        _errors = meter.CreateCounter<long>(
            "ihub.errors",
            unit: "{error}",
            description: "Requests that failed with a server-side error, by error type.");

        _ingestLag = meter.CreateHistogram<double>(
            "ihub.activity.ingest_lag",
            unit: "s",
            description: "Time from when an activity occurred to when it was committed and became visible in statements.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = LagBuckets });

        _knownSourceSystems = new HashSet<string>(options.Value.KnownSourceSystems, StringComparer.Ordinal);
    }

    public void ActivityProcessed(string sourceSystem, IngestOutcome outcome) =>
        _activitiesProcessed.Add(
            1,
            new KeyValuePair<string, object?>("source_system", SourceSystemTag(sourceSystem)),
            new KeyValuePair<string, object?>("outcome", outcome.ToTagValue()));

    public void IngestLagObserved(string sourceSystem, TimeSpan lag) =>
        _ingestLag.Record(
            Math.Max(0, lag.TotalSeconds),
            new KeyValuePair<string, object?>("source_system", SourceSystemTag(sourceSystem)));

    public void BatchRejected(string reason) =>
        _batchesRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void ErrorOccurred(string errorType) =>
        _errors.Add(1, new KeyValuePair<string, object?>("error_type", errorType));

    // Bounded to configured values to cap metric cardinality.
    private string SourceSystemTag(string sourceSystem) =>
        _knownSourceSystems.Contains(sourceSystem) ? sourceSystem : OtherSourceSystem;
}
