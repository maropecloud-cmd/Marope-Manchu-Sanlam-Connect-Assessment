using System.Globalization;
using System.Text;

namespace IHub.Simulator;

internal static class Report
{
    private const string Bars = "▁▂▃▄▅▆▇█";

    public static string Render(RunResult run)
    {
        var o = run.Options;
        var s = run.Stats;
        var w = run.Workload;
        var acknowledged = s.Outcomes.Values.Sum();
        var sb = new StringBuilder();

        void Line(string text = "") => sb.AppendLine(text);

        Line("# IHub simulation report");
        Line();
        Line(run.Passed
            ? $"**PASS**: all {w.IntermediaryIds.Count:N0} statements match the reference model exactly."
            : $"**FAIL**: {run.Mismatches.Count} statement mismatch(es), {s.AbandonedBatches} abandoned and {s.RejectedBatches} rejected batch(es), " +
              $"{s.Outcomes.GetValueOrDefault("Conflict")} conflict(s).");
        Line();

        Line("| Workload | |");
        Line("| --- | --- |");
        Line($"| Seed | `{o.Seed}` (replay with `--seed {o.Seed}`) |");
        Line($"| Activities / revisions | {o.Activities:N0} / {w.Revisions.Count:N0} (up to {o.MaxVersions} versions each, incl. reversals and moves) |");
        Line($"| Deliveries | {w.Deliveries.Count:N0} ({(w.Deliveries.Count - w.Revisions.Count) * 100.0 / w.Revisions.Count:0}% duplicates, shuffled out of order) |");
        Line($"| Intermediaries | {o.Intermediaries:N0} |");
        Line($"| Batching | {o.BatchSize} per request, {o.Concurrency} concurrent producers, rate {(o.Rate > 0 ? $"{o.Rate:N0}/s" : "unthrottled")} |");
        Line($"| Chaos | {(o.RestartDatabaseAfter is { } d ? $"database container restarted at {d.TotalSeconds:0}s" : "none")} |");
        Line($"| Statement window | {run.WindowFrom:yyyy-MM-dd HH:mm} to {run.WindowTo:yyyy-MM-dd HH:mm} UTC |");
        Line();

        Line("## Throughput");
        Line();
        Line($"{acknowledged:N0} activities acknowledged in {s.LoadDuration.TotalSeconds:0.0} s: " +
             $"**{acknowledged / s.LoadDuration.TotalSeconds:N0} activities/s** " +
             $"({acknowledged / s.LoadDuration.TotalSeconds * 3600:N0}/hour).");
        Line();
        Line("Acknowledged per second:");
        Line();
        Line("```");
        Line(Sparkline(s.AcknowledgedPerSecond, (int)Math.Ceiling(s.LoadDuration.TotalSeconds)));
        Line("```");
        Line();

        Line("## Latency (ms)");
        Line();
        Line("| | p50 | p95 | p99 | max |");
        Line("| --- | ---: | ---: | ---: | ---: |");
        Line(LatencyRow("Each HTTP attempt", s.AttemptLatenciesMs));
        Line(LatencyRow("Batch: first send to ack, incl. retries", s.BatchLatenciesMs));
        Line();

        Line("## Responses and outcomes");
        Line();
        Line("| HTTP responses | Count |");
        Line("| --- | ---: |");
        foreach (var (kind, count) in s.Responses.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            Line($"| {kind} | {count:N0} |");
        }

        Line($"| Producer retries | {s.Retries:N0} |");
        Line();
        Line("| Ingestion outcome | Count |");
        Line("| --- | ---: |");
        foreach (var outcome in new[] { "Created", "Updated", "Duplicate", "Stale", "Conflict" })
        {
            Line($"| {outcome} | {s.Outcomes.GetValueOrDefault(outcome):N0} |");
        }

        Line();
        Line("## Timeline");
        Line();
        Line("```");
        foreach (var (at, message) in s.Timeline)
        {
            Line($"{at.TotalSeconds,7:0.0}s  {message}");
        }

        Line("```");

        if (run.Mismatches.Count > 0)
        {
            Line();
            Line("## Mismatches (first 20)");
            Line();
            foreach (var m in run.Mismatches.Take(20))
            {
                Line($"- `{m.IntermediaryId}`: got [{m.Actual}], expected [{m.Expected}]");
            }
        }

        return sb.ToString();
    }

    private static string LatencyRow(string label, IEnumerable<double> samples)
    {
        var sorted = samples.Order().ToArray();
        if (sorted.Length == 0)
        {
            return $"| {label} | - | - | - | - |";
        }

        string P(double p) => sorted[Math.Max(0, (int)Math.Ceiling(p * sorted.Length) - 1)].ToString("N0", CultureInfo.InvariantCulture);
        return $"| {label} | {P(0.50)} | {P(0.95)} | {P(0.99)} | {sorted[^1].ToString("N0", CultureInfo.InvariantCulture)} |";
    }

    private static string Sparkline(IReadOnlyDictionary<int, int> perSecond, int seconds)
    {
        var values = Enumerable.Range(0, Math.Max(1, seconds)).Select(i => perSecond.GetValueOrDefault(i)).ToArray();
        var max = Math.Max(1, values.Max());
        var line = new string([.. values.Select(v => v == 0 ? '·' : Bars[Math.Min(Bars.Length - 1, v * Bars.Length / (max + 1))])]);
        return $"{line}\npeak {max:N0}/s; '·' = nothing acknowledged in that second";
    }
}
