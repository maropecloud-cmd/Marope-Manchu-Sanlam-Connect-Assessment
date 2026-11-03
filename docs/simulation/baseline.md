# IHub simulation report

**PASS**: all 200 statements match the reference model exactly.

| Workload | |
| --- | --- |
| Seed | `42` (replay with `--seed 42`) |
| Activities / revisions | 5,000 / 12,521 (up to 4 versions each, incl. reversals and moves) |
| Deliveries | 17,756 (42% duplicates, shuffled out of order) |
| Intermediaries | 200 |
| Batching | 25 per request, 16 concurrent producers, rate unthrottled |
| Chaos | none |
| Statement window | 2026-11-03 16:59 to 2026-11-03 22:59 UTC |

## Throughput

17,756 activities acknowledged in 6.9 s: **2,583 activities/s** (9,298,068/hour).

Acknowledged per second:

```
▄▆▇▇██▇
peak 3,200/s; '·' = nothing acknowledged in that second
```

## Latency (ms)

| | p50 | p95 | p99 | max |
| --- | ---: | ---: | ---: | ---: |
| Each HTTP attempt | 147 | 223 | 239 | 287 |
| Batch: first send to ack, incl. retries | 147 | 223 | 244 | 287 |

## Responses and outcomes

| HTTP responses | Count |
| --- | ---: |
| 200 | 711 |
| Producer retries | 0 |

| Ingestion outcome | Count |
| --- | ---: |
| Created | 5,000 |
| Updated | 3,113 |
| Duplicate | 2,689 |
| Stale | 6,954 |
| Conflict | 0 |

## Timeline

```
    0.0s  Load started
    0.0s  /health/ready: ready
    6.9s  Load finished: all 711 batches acknowledged or abandoned
    7.0s  Reconciliation finished: 0 mismatch(es)
```
