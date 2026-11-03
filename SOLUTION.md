# IHub Intermediary Activity Statement: Solution Design

## 1. Decisions at a glance

| Area | Decision | Reason |
| --- | --- | --- |
| Stack | .NET 10 (LTS) Minimal APIs, EF Core 10, SQL Server 2022 | Fits the existing .NET estate; little framework code for two endpoints |
| Ingestion | One HTTP endpoint: `POST /api/v1/activities`, batches of 1-500 | Producers batch naturally; the same code later sits behind an SQS consumer |
| Retrieval | One HTTP endpoint: `GET /api/v1/intermediaries/{id}/statement?from&to` | A statement is a read-only view |
| Identity and ordering | `(sourceSystem, sourceActivityId)`; source-assigned `version`, highest wins | Deterministic and independent of clocks or arrival order |
| Cancellations | A new version with `status = Reversed` | One row per activity; a reversal is just another revision |
| Statement | Computed on read with one grouped query on a covering index | Always consistent with the stored data; nothing to keep in sync |
| Processing | Synchronous: committed means visible in the next statement | Lowest possible delay, one fewer moving part |
| Money and time | `decimal(19,4)`, per-currency totals, no conversion; UTC, offsets required, window `[from, to)` | Exact arithmetic; no ambiguous timestamps; adjacent windows never overlap |
| Observability | JSON logs with correlation IDs, OpenTelemetry metrics at `/metrics`, liveness and readiness probes | Answers "is it working, how fast, what broke" with no extra tooling |

## 2. Assumptions

| # | Assumption | Effect on the design |
| --- | --- | --- |
| A1 | Each source gives every activity a stable ID. | Identity is `(sourceSystem, sourceActivityId)`. |
| A2 | A source can number an activity's revisions (row version, sequence, event number). | Out-of-order arrival is resolved by version, not time. |
| A3 | An activity is `Active` or `Reversed`; a cancellation is a new revision. | Only `Active` activities count; reversals are reported separately. |
| A4 | Amounts are non-negative; clawbacks are their own activity type. | The gross total is genuinely gross. |
| A5 | Brokers can earn in several currencies, and IHub must not convert. | Totals are per currency. |
| A6 | A statement window refers to when the activity happened (`occurredAt`). | A late arrival can change a past window; `lastUpdatedAt` shows when it did. |
| A7 | Identifiers are opaque and case-sensitive. | Binary collation on key columns: `POL-1` and `pol-1` are different activities. |
| A8 | Peak load is thousands of activities per hour, with bursts. | Synchronous processing in the request is enough; no queue inside the service. |
| A9 | Authentication is handled in front of the service (gateway or identity provider). | No auth code in the slice. |

## 3. API

Versioned in the path. Errors are RFC 9457 problem details. Every request accepts and returns `X-Correlation-ID`.

### 3.1 `POST /api/v1/activities`

```json
{ "activities": [ {
    "sourceSystem": "life-admin", "sourceActivityId": "POL-1001-COMM", "version": 2,
    "intermediaryId": "BRK-001", "activityType": "commission", "status": "Active",
    "amount": 1500.00, "currency": "ZAR", "occurredAt": "2026-10-05T09:15:00+02:00" } ] }
```

The response gives each item's outcome in request order, so producers can reconcile:

| Outcome | Meaning | Changes statement |
| --- | --- | --- |
| `Created` | First revision of this activity | Yes |
| `Updated` | Higher version than stored; replaces it | Yes |
| `Duplicate` | Same version and content as stored (retry or redelivery) | No |
| `Stale` | Lower version than stored (arrived out of order) | No |
| `Conflict` | Same version, different content: a producer bug. Not applied; logged and counted | No |

`200` means the batch was processed. `400` means validation failed and nothing was applied; errors name the field, e.g. `activities[3].amount`. `503` (with `Retry-After`) means the database is unavailable and the whole batch can be resent. `500` includes the `correlationId`.

A batch is validated as a unit: partial acceptance would force producers to track which items went through. Because processing is idempotent, the producer rule is simply "fix and resend the whole batch".

### 3.2 `GET /api/v1/intermediaries/{id}/statement?from=...&to=...`

```json
{ "intermediaryId": "BRK-001",
  "window": { "from": "2026-10-01T00:00:00+00:00", "to": "2026-11-01T00:00:00+00:00" },
  "totals": [ { "currency": "ZAR", "grossAmount": 2320.5000, "activityCount": 2, "reversedActivityCount": 0 } ],
  "lastUpdatedAt": "2026-11-02T13:20:01.512+00:00",
  "generatedAt": "2026-11-02T13:25:44.090+00:00" }
```

`grossAmount` sums `Active` activities whose `occurredAt` is in `[from, to)`. `lastUpdatedAt` is when any activity in the window last changed, including reversals (`null` if none). `generatedAt` is the point in time the statement reflects. Both bounds are required (statement periods are a business definition), and a window may span at most 366 days. An intermediary with no activity gets an empty `200`: without an intermediary registry, unknown and inactive cannot be told apart.

### 3.3 Operational endpoints

`/health/live` (process up, no dependency checks, so a database outage never restarts healthy containers), `/health/ready` (database reachable; the load balancer target) `/metrics` (Prometheus format) and `/scalar` (API reference, enabled by configuration).

## 4. Data model

One current-state table, one row per activity.

| Column | Type | Purpose |
| --- | --- | --- |
| `Id` | `bigint identity`, clustered PK | Burst inserts append instead of splitting pages |
| `SourceSystem`, `SourceActivityId` | `varchar(50)`, `varchar(100)`, BIN2 | Identity |
| `Version` | `bigint`, check `>= 1` | Orders revisions |
| `IntermediaryId` | `varchar(50)` BIN2 | Statement owner |
| `ActivityType` | `varchar(50)` | Free-form business type |
| `Status` | `varchar(16)`, check | `Active` or `Reversed` |
| `Amount`, `Currency` | `decimal(19,4)` check `>= 0`, `char(3)` | Exact money |
| `OccurredAt` | `datetimeoffset` (UTC) | Statement windowing |
| `ContentHash` | `binary(32)` | SHA-256 of business fields; tells `Duplicate` from `Conflict` |
| `FirstReceivedAt`, `UpdatedAt` | `datetimeoffset` | Receipt time; drives `lastUpdatedAt` |
| `LastCorrelationId` | `varchar(64)` | Links a row to the request (and logs) that last changed it |

| Index | Definition | Used for |
| --- | --- | --- |
| `UX_Activities_SourceKey` | Unique `(SourceSystem, SourceActivityId)` | The idempotency guarantee |
| `IX_Activities_Intermediary_OccurredAt` | `(IntermediaryId, OccurredAt)` INCLUDE `(Status, Currency, Amount, UpdatedAt)` | The statement query, answered from the index alone |

Choices made for real-world data:

- **Binary collation on identifiers.** SQL Server's default is case-insensitive and would silently merge `pol-1` and `POL-1`.
- **ASCII-only identifiers in `varchar`.** SQL Server replaces non-ASCII characters in `varchar`, which could make two IDs equal.
- **Check constraints** (`Version >= 1`, `Amount >= 0`, valid `Status`). They duplicate API validation on purpose, to protect against backfills and scripts.
- **Read Committed Snapshot Isolation**, enabled in the initial migration. Statement reads see a consistent snapshot and never block, or are blocked by, ingestion.

Schema changes are EF Core migrations. Locally they are applied at startup before Kestrel listens, so readiness cannot pass against an old schema. In production they run as a separate deployment step.

## 5. Correctness: duplicates, out-of-order arrival, concurrency

### 5.1 Write algorithm ([ActivityIngestor.cs](src/IHub.Api/Features/Activities/ActivityIngestor.cs))

```
1. INSERT the revision                                   -> Created
     unique-key violation: the activity exists, go to 2
2. UPDATE ... SET <fields>, Version = @v
   WHERE SourceSystem = @s AND SourceActivityId = @id AND Version < @v
     1 row                                               -> Updated
     0 rows: stored version is the same or newer, go to 3
3. SELECT Version, ContentHash (reporting only)
     stored version > @v                                 -> Stale
     same version, same hash                             -> Duplicate
     same version, different hash                        -> Conflict
```

Steps 1 and 2 decide what is stored; step 3 only explains the outcome to the producer.

### 5.2 Why it holds under concurrency

| Race | What SQL Server does | Result |
| --- | --- | --- |
| Two inserts of a new activity | The unique index admits one; the other gets error 2627/2601 and goes to step 2 | One row |
| Two updates (v2, v3) | The row's update lock serialises them; the second re-evaluates `Version < @v` against the committed row | v3 is stored, whichever arrives first |
| An older or repeated version racing a newer one | Its predicate is false against the newer row | `Stale` or `Duplicate` |
| A statement read during ingestion | RCSI serves a consistent committed snapshot without blocking | Totals as of one point in time |
| Several service instances | No in-memory locks or caches; all coordination is in the database | Scale out without changes |

Concurrent submissions for the **same intermediary** need nothing extra: different activities are different rows, and there is no per-intermediary total to contend on.

### 5.3 Retries and partial failure

Revisions in a batch commit one by one. If a batch fails midway, the producer resends all of it; revisions that already committed come back as `Duplicate`. EF Core's `EnableRetryOnFailure` retries transient database errors, which is safe for the same reason: a retried insert that had in fact committed hits the unique key. The unique-key violations behind normal duplicate handling are logged at `Debug`, so they never page anyone.

## 6. How the statement is derived

[StatementQuery.cs](src/IHub.Api/Features/Statements/StatementQuery.cs):

```sql
SELECT Currency,
       SUM(CASE WHEN Status = 'Active' THEN Amount ELSE 0 END),
       COUNT(CASE WHEN Status = 'Active' THEN 1 END),
       COUNT(CASE WHEN Status = 'Reversed' THEN 1 END),
       MAX(UpdatedAt)
FROM   Activities
WHERE  IntermediaryId = @id AND OccurredAt >= @from AND OccurredAt < @to
GROUP  BY Currency;
```

It is a seek on the covering index. Computing on read means there is no second copy of the data to keep atomic, compensate when a version changes the amount or broker, or rebuild after drift. Activities are visible as soon as they commit.

If reads become heavy (a portal polling every few seconds) or windows span years of busy data, the next step is a daily summary table `(IntermediaryId, Currency, Day)` updated in the same transaction as the activity write. The write path already has the old and new state of each activity, so the API would not change.

## 7. Evidence: correctness under load and failure

### 7.1 Unit tests

Unit tests cover validation rules, strict timestamp parsing, content hashing, and the SQL shape of the statement query: a single `SELECT`, aggregated in SQL and filtered on the index keys, so a refactor cannot quietly move the aggregation into memory.

### 7.2 Integration tests

Integration tests run the real API in-process against SQL Server in a container (Testcontainers), because unique-key and locking behaviour can only be tested on the real engine. Each test uses its own intermediary, so tests are independent while sharing one database.

| Test | Scenario |
| --- | --- |
| `Resubmitting_the_same_revision_is_counted_once` | The same revision sent three times: `Created`, `Duplicate`, `Duplicate`; counted once |
| `Duplicates_within_one_batch_are_counted_once` | The same revision twice in one batch |
| `Reversal_that_arrives_before_the_original_is_not_undone_by_it` | v2 `Reversed` arrives before v1 `Active`; v1 is `Stale` and the total is 0 |
| `Newer_version_replaces_the_stored_state` | v1 for 100, then v2 for 250; the total is 250 |
| `Same_version_with_different_content_is_flagged_as_conflict_and_not_applied` | A producer bug is reported and the stored revision kept |
| `Concurrent_submissions_for_one_intermediary_are_all_counted_exactly_once` | 40 activities, each sent twice, as 80 parallel requests; the sum is exact |
| `Concurrent_versions_of_one_activity_converge_on_the_highest_version` | 25 versions, each sent twice in random parallel order; one `Created`, v25 stored |
| `Activity_moved_to_another_intermediary_leaves_the_first_statement` | A newer version with a different broker moves the amount |
| `StatementTests` | Half-open window boundaries, time zone offsets, per-currency totals, `lastUpdatedAt` on reversal, empty windows, invalid windows |

## 8. Observability

### 8.1 Signals

| Signal | Answers |
| --- | --- |
| `/health/ready`, `/health/live` | Can this instance take traffic? Is the process alive? |
| `http.server.request.duration` (route, status), built in | Throughput, latency percentiles, error rate per endpoint |
| `ihub.activities.processed` (outcome, source) | Volume, and the mix of duplicates, stale and conflicts per producer |
| `ihub.activity.ingest_lag` (source), histogram | **Processing delay**: from `occurredAt` to commit, which is when the activity appears in statements |
| `ihub.ingest.batches.rejected` (reason) | Is a producer sending invalid data? |
| `ihub.errors` (error type) | Which server-side failures, e.g. `database_unavailable` |
| Runtime metrics | GC, thread pool, exceptions |
| JSON logs with `CorrelationId` | What happened to this request? |

Ingest lag is tagged by source, which separates "an upstream system is late" from "IHub is behind". The `source_system` tag is limited to a configured list (anything else is `other`), so a misbehaving producer cannot create unbounded metric series.

### 8.2 Logs and end-to-end correlation

A caller's `X-Correlation-ID` is adopted if log-safe, otherwise generated. It is echoed in the response, added to every log line's scope (with W3C trace and span IDs) and included in error bodies. Each activity row stores `LastCorrelationId`, so the trail also runs from data to logs: for a suspicious statement line, find the request that last changed it. Log events have stable IDs (4001 activity outcome, 4002 conflict, 3001 server error, and so on). Probe and scrape requests log at `Debug`. Integration tests cover the health probes, the metrics output and correlation ID propagation.

```json
{"Timestamp":"2026-11-02T13:20:01.512Z","EventId":4001,"LogLevel":"Information","Message":"Activity Updated: life-admin/POL-1001-COMM v3 for intermediary BRK-DEMO-001","State":{"Outcome":"Updated","SourceSystem":"life-admin","Version":3},"Scopes":[{"TraceId":"4bf92f35..."},{"CorrelationId":"demo-step-4"}]}
```

### 8.3 Alerts (CloudWatch, from scraped metrics plus ALB and SQS metrics)

| Alert | Condition | Operator action |
| --- | --- | --- |
| **Rising error rate** (page) | 5xx above 2% of requests for 5 min, or `ihub.errors{error_type="database_unavailable"}` above 0 for 3 min, or fewer than 2 healthy targets | Check `/health/ready` and RDS events (failover, CPU, storage). If it started with a deployment, roll back. Use a failing response's `correlationId` to find the exception |
| **Growing processing delay** (page) | p95 `ihub.activity.ingest_lag` above 15 min for 15 min, or SQS oldest message older than 10 min | One `source_system` late: upstream issue, contact its owners. All late with a growing queue: IHub is behind, so check scaling and RDS load. Check the DLQ for poison messages |
| Producer conflicts (ticket) | Any `outcome="conflict"` within an hour | A source reused a version with different content; raise it with the owning team |

### 7.3 Property test: convergence against a reference model

Example tests check the interleavings someone thought of. [ConvergencePropertyTests](tests/IHub.Api.Tests/Integration/ConvergencePropertyTests.cs) checks the rule itself:

- A seeded [generator](tools/IHub.Simulator/Workload/WorkloadGenerator.cs) creates activity histories with corrections, reversals, moves between brokers, version gaps (1, 2, 4), time corrections across window boundaries, and the same instant written in UTC or SAST.
- Each revision is redelivered with 40% probability, everything is shuffled, cut into random batches and sent with 12 concurrent producers.
- The result is compared with a [reference model](tools/IHub.Simulator/Workload/ReferenceModel.cs) about ten lines long ("keep the highest version, sum the active ones in the window"). Every stored row and every statement must match it, with exactly one `Created` per activity and no `Conflict`.

Five fixed seeds run on every build. `IHUB_PROPERTY_SEEDS=random:50 dotnet test` explores new ones; 50 random seeds passed. A failure prints the seed and the exact diverging rows, and `IHUB_PROPERTY_SEEDS=<seed>` replays it.

**Does the test catch real bugs?** I changed the version guard from `Version < @v` to `Version != @v`, a plausible slip that lets older versions overwrite newer ones. The test failed, listing activities such as `stored v1, expected v4`.
