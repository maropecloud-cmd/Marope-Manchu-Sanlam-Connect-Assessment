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
