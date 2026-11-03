using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IHub.Workload;

namespace IHub.Simulator;

internal sealed class RunStats
{
    private int _retries;
    private int _abandonedBatches;
    private int _rejectedBatches;

    public ConcurrentBag<double> AttemptLatenciesMs { get; } = [];

    public ConcurrentBag<double> BatchLatenciesMs { get; } = [];

    public ConcurrentDictionary<string, int> Responses { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, int> Outcomes { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<int, int> AcknowledgedPerSecond { get; } = new();

    public ConcurrentQueue<(TimeSpan At, string Event)> Timeline { get; } = new();

    public int Retries => _retries;

    public int AbandonedBatches => _abandonedBatches;

    public int RejectedBatches => _rejectedBatches;

    public TimeSpan LoadDuration { get; set; }

    public void Retry() => Interlocked.Increment(ref _retries);

    public void Abandoned() => Interlocked.Increment(ref _abandonedBatches);

    public void Rejected() => Interlocked.Increment(ref _rejectedBatches);

    public void Response(string kind) => Responses.AddOrUpdate(kind, 1, (_, n) => n + 1);
}

internal sealed record StatementMismatch(string IntermediaryId, string Actual, string Expected);

internal sealed record RunResult(
    SimulatorOptions Options,
    ActivityWorkload Workload,
    DateTimeOffset WindowFrom,
    DateTimeOffset WindowTo,
    RunStats Stats,
    IReadOnlyList<StatementMismatch> Mismatches)
{
    public bool Passed => Mismatches.Count == 0 && Stats.AbandonedBatches == 0 && Stats.RejectedBatches == 0
                          && Stats.Outcomes.GetValueOrDefault("Conflict") == 0;
}

internal sealed class LoadRunner(SimulatorOptions options, HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly RunStats _stats = new();
    private readonly Stopwatch _clock = new();

    public async Task<RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var windowTo = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        var windowFrom = windowTo.AddHours(-6);

        var workload = WorkloadGenerator.Generate(
            new WorkloadOptions
            {
                RunId = $"SIM{options.Seed:X8}-{now:HHmmss}",
                Activities = options.Activities,
                Intermediaries = options.Intermediaries,
                MaxVersions = options.MaxVersions,
                DuplicateRate = options.DuplicateRate,
                WindowFrom = windowFrom,
                WindowTo = windowTo,
            },
            options.Seed);

        var batches = workload.Deliveries.Chunk(options.BatchSize).ToList();
        Console.WriteLine(
            $"Workload: {workload.Revisions.Count:N0} revisions of {options.Activities:N0} activities, " +
            $"{workload.Deliveries.Count:N0} deliveries in {batches.Count:N0} batches (seed {options.Seed}).");

        using var stopBackground = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _clock.Start();
        Mark("Load started");

        var readiness = WatchReadinessAsync(stopBackground.Token);
        var chaos = options.RestartDatabaseAfter is { } delay
            ? RestartDatabaseAsync(delay, stopBackground.Token)
            : Task.CompletedTask;

        await Parallel.ForEachAsync(
            batches.Select((batch, index) => (batch, index)),
            new ParallelOptions { MaxDegreeOfParallelism = options.Concurrency, CancellationToken = cancellationToken },
            async (item, token) =>
            {
                await PaceAsync(item.index, token);
                await SendWithRetryAsync(item.batch, token);
            });

        _stats.LoadDuration = _clock.Elapsed;
        Mark($"Load finished: all {batches.Count:N0} batches acknowledged or abandoned");

        await chaos;
        await stopBackground.CancelAsync();
        await readiness;

        Console.WriteLine($"Reconciling {workload.IntermediaryIds.Count:N0} statements against the reference model...");
        var mismatches = await ReconcileAsync(workload, windowFrom, windowTo, cancellationToken);
        Mark($"Reconciliation finished: {mismatches.Count} mismatch(es)");

        return new RunResult(options, workload, windowFrom, windowTo, _stats, mismatches);
    }

    private async Task PaceAsync(int batchIndex, CancellationToken token)
    {
        if (options.Rate <= 0)
        {
            return;
        }

        var due = TimeSpan.FromSeconds(batchIndex * options.BatchSize / options.Rate);
        var wait = due - _clock.Elapsed;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, token);
        }
    }

    private async Task SendWithRetryAsync(GeneratedRevision[] batch, CancellationToken token)
    {
        var body = new { activities = batch.Select(ToWire) };
        var firstSent = _clock.Elapsed;
        var attempt = 0;

        while (true)
        {
            var started = _clock.Elapsed;
            TimeSpan? retryAfter = null;
            try
            {
                using var response = await http.PostAsJsonAsync("/api/v1/activities", body, Json, token);
                _stats.AttemptLatenciesMs.Add((_clock.Elapsed - started).TotalMilliseconds);
                _stats.Response(((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var result = await response.Content.ReadFromJsonAsync<SubmitResponse>(Json, token);
                    foreach (var item in result!.Results)
                    {
                        _stats.Outcomes.AddOrUpdate(item.Outcome, 1, (_, n) => n + 1);
                    }

                    _stats.BatchLatenciesMs.Add((_clock.Elapsed - firstSent).TotalMilliseconds);
                    _stats.AcknowledgedPerSecond.AddOrUpdate((int)_clock.Elapsed.TotalSeconds, batch.Length, (_, n) => n + batch.Length);
                    return;
                }

                if ((int)response.StatusCode < 500)
                {
                    _stats.Rejected();
                    Mark($"Batch rejected with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(token)}");
                    return;
                }

                retryAfter = response.Headers.RetryAfter?.Delta;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
            {
                _stats.Response("transport error");
            }

            if (_clock.Elapsed - firstSent > options.RetryBudget)
            {
                _stats.Abandoned();
                Mark("Batch abandoned: retry budget exhausted");
                return;
            }

            attempt++;
            _stats.Retry();
#pragma warning disable CA5394 // Jitter, not security.
            var backoff = TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)Math.Min(5_000, 100 * Math.Pow(2, attempt))));
#pragma warning restore CA5394
            await Task.Delay(retryAfter is { } asked && asked > backoff ? asked : backoff, token);
        }
    }

    private async Task RestartDatabaseAsync(TimeSpan delay, CancellationToken token)
    {
        await Task.Delay(delay, token);
        Mark($"CHAOS: restarting database container '{options.DatabaseContainer}'");

        var startInfo = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        startInfo.ArgumentList.Add("restart");
        startInfo.ArgumentList.Add(options.DatabaseContainer);

        using var process = Process.Start(startInfo)!;
        await process.WaitForExitAsync(token);
        Mark(process.ExitCode == 0
            ? "CHAOS: database container restarted (SQL Server now recovering)"
            : $"CHAOS: docker restart failed: {(await process.StandardError.ReadToEndAsync(token)).Trim()}");
    }

    private async Task WatchReadinessAsync(CancellationToken token)
    {
        string? last = null;
        while (!token.IsCancellationRequested)
        {
            string state;
            try
            {
                using var probe = CancellationTokenSource.CreateLinkedTokenSource(token);
                probe.CancelAfter(TimeSpan.FromSeconds(3));
                using var response = await http.GetAsync(new Uri("/health/ready", UriKind.Relative), probe.Token);
                state = response.IsSuccessStatusCode ? "ready" : $"NOT ready ({(int)response.StatusCode})";
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                state = "NOT ready (no response)";
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (state != last)
            {
                Mark($"/health/ready: {state}");
                last = state;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<IReadOnlyList<StatementMismatch>> ReconcileAsync(
        ActivityWorkload workload, DateTimeOffset from, DateTimeOffset to, CancellationToken token)
    {
        var expected = ReferenceModel.ExpectedStatements(workload.IntermediaryIds, workload.Revisions, from, to);
        var mismatches = new ConcurrentBag<StatementMismatch>();
        var query = $"from={Uri.EscapeDataString(from.ToString("O", CultureInfo.InvariantCulture))}" +
                    $"&to={Uri.EscapeDataString(to.ToString("O", CultureInfo.InvariantCulture))}";

        await Parallel.ForEachAsync(
            expected,
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = token },
            async (pair, ct) =>
            {
                var statement = await http.GetFromJsonAsync<StatementWire>(
                    new Uri($"/api/v1/intermediaries/{pair.Key}/statement?{query}", UriKind.Relative), Json, ct);
                var actual = statement!.Totals
                    .Select(t => new ExpectedTotal(t.Currency, t.GrossAmount, t.ActivityCount, t.ReversedActivityCount))
                    .ToList();

                if (!actual.SequenceEqual(pair.Value))
                {
                    mismatches.Add(new StatementMismatch(pair.Key, string.Join("; ", actual), string.Join("; ", pair.Value)));
                }
            });

        return [.. mismatches.OrderBy(m => m.IntermediaryId, StringComparer.Ordinal)];
    }

    private void Mark(string message)
    {
        var at = _clock.Elapsed;
        _stats.Timeline.Enqueue((at, message));
        Console.WriteLine($"[{at.TotalSeconds,7:0.0}s] {message}");
    }

    private static object ToWire(GeneratedRevision r) => new
    {
        sourceSystem = r.SourceSystem,
        sourceActivityId = r.SourceActivityId,
        version = r.Version,
        intermediaryId = r.IntermediaryId,
        activityType = r.ActivityType,
        status = r.Status,
        amount = r.Amount,
        currency = r.Currency,
        occurredAt = r.OccurredAtText,
    };

    private sealed record SubmitResponse(IReadOnlyList<SubmitResult> Results);

    private sealed record SubmitResult(string Outcome);

    private sealed record StatementWire(IReadOnlyList<TotalWire> Totals);

    private sealed record TotalWire(string Currency, decimal GrossAmount, int ActivityCount, int ReversedActivityCount);
}
