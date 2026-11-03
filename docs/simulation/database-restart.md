# IHub simulation report

**PASS**: all 200 statements match the reference model exactly.

| Workload | |
| --- | --- |
| Seed | `7` (replay with `--seed 7`) |
| Activities / revisions | 6,000 / 14,884 (up to 4 versions each, incl. reversals and moves) |
| Deliveries | 21,283 (43% duplicates, shuffled out of order) |
| Intermediaries | 200 |
| Batching | 25 per request, 16 concurrent producers, rate 400/s |
| Chaos | database container restarted at 15s |
| Statement window | 2026-11-03 16:59 to 2026-11-03 22:59 UTC |

## Throughput

21,283 activities acknowledged in 53.2 s: **400 activities/s** (1,438,990/hour).

Acknowledged per second:

```
▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▁···▂█▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▂▁
peak 2,025/s; '·' = nothing acknowledged in that second
```

## Latency (ms)

| | p50 | p95 | p99 | max |
| --- | ---: | ---: | ---: | ---: |
| Each HTTP attempt | 74 | 171 | 4,223 | 4,718 |
| Batch: first send to ack, incl. retries | 74 | 171 | 4,417 | 5,361 |

## Responses and outcomes

| HTTP responses | Count |
| --- | ---: |
| 200 | 852 |
| 503 | 3 |
| Producer retries | 3 |

| Ingestion outcome | Count |
| --- | ---: |
| Created | 5,991 |
| Updated | 3,545 |
| Duplicate | 3,459 |
| Stale | 8,288 |
| Conflict | 0 |

## Timeline

```
    0.0s  Load started
    0.0s  /health/ready: ready
   15.0s  CHAOS: restarting database container 'ihub-sqlserver-1'
   16.3s  /health/ready: NOT ready (503)
   16.7s  CHAOS: database container restarted (SQL Server now recovering)
   19.8s  /health/ready: NOT ready (no response)
   20.7s  /health/ready: ready
   53.2s  Load finished: all 852 batches acknowledged or abandoned
   53.5s  Reconciliation finished: 0 mismatch(es)
```
