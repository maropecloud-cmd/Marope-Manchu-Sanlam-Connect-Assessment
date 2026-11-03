# IHub Intermediary Activity Statement

A small .NET 10 service that ingests activity from upstream product systems and returns an intermediary's activity statement (gross totals over a time window). It is built to stay correct when activities arrive **more than once**, **out of order** and **concurrently**.

- Design, decisions and trade-offs: **[SOLUTION.md](SOLUTION.md)**
- Stack: C# / .NET 10 (ASP.NET Core Minimal APIs), EF Core 10, SQL Server 2022, OpenTelemetry, xUnit + Testcontainers

---

## Quick start (Docker only)

The only prerequisite is Docker (Docker Desktop, or Docker Engine with Compose v2).

```bash
git clone <this-repo-url> ihub && cd ihub
docker compose up --build
```

When the log shows `Database migrations applied`, open **http://localhost:8080**. It redirects to the interactive API reference (Scalar), where both endpoints can be called from the browser.

The first start takes 1-2 minutes: Docker pulls the SQL Server image (about 1.5 GB) and waits for the database to report healthy before starting the API. The API creates the database and applies migrations on startup.

To stop: `Ctrl+C`, then `docker compose down` (add `-v` to delete the database volume).

## Try it

**Option 1: the API reference.** Open http://localhost:8080/scalar.

**Option 2: the scripted scenarios.** [IHub.http](IHub.http) walks through the interesting cases in order: first submission, safe retry, a cancellation overtaking a correction, a producer conflict and a validation failure. Run it with the VS Code *REST Client* extension, Visual Studio or Rider.

**Option 3: curl.**

```bash
# Submit an activity (repeat this command: the second response says "Duplicate" and the total does not change)
curl -s -X POST http://localhost:8080/api/v1/activities \
  -H "Content-Type: application/json" \
  -H "X-Correlation-ID: readme-demo-1" \
  -d '{"activities":[{"sourceSystem":"life-admin","sourceActivityId":"POL-1001-COMM","version":1,
       "intermediaryId":"BRK-001","activityType":"commission","status":"Active",
       "amount":1500.00,"currency":"ZAR","occurredAt":"2026-10-05T09:15:00+02:00"}]}'

# Get the statement for October 2026 (UTC)
curl -s "http://localhost:8080/api/v1/intermediaries/BRK-001/statement?from=2026-10-01T00:00:00Z&to=2026-11-01T00:00:00Z"
```

Watch the API container's console. Every log line is structured JSON and carries the `CorrelationId` you sent.

## Endpoints

| Method | Path                                                        | Purpose                                                     |
| ------ | ----------------------------------------------------------- | ----------------------------------------------------------- |
| POST   | `/api/v1/activities`                                        | Submit a batch of 1-500 activity revisions (idempotent)     |
| GET    | `/api/v1/intermediaries/{intermediaryId}/statement?from&to` | Gross totals per currency for the window `[from, to)`       |
| GET    | `/health/live`                                              | Liveness: the process is up                                 |
| GET    | `/health/ready`                                             | Readiness: the database is reachable (load balancer target) |
| GET    | `/metrics`                                                  | Prometheus-format metrics                                   |
| GET    | `/scalar`, `/openapi/v1.json`                               | API reference and OpenAPI document                          |

Timestamps must include an offset (`Z` or `+02:00`). In a query string, `+` must be URL-encoded as `%2B`, or use `Z`.

## Run from source (for debugging)

Requires the .NET 10 SDK (pinned in [global.json](global.json)).

```bash
docker compose up -d sqlserver          # database only
dotnet run --project src/IHub.Api       # API on http://localhost:8080, Development settings
```

`appsettings.Development.json` points at the containerised database and enables auto-migration and the API reference.

## Tests

```bash
dotnet test
```

| Suite       | What it covers                                                                                                                                     | Needs                   |
| ----------- | -------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------- |
| Unit        | Validation rules, strict timestamp parsing, content hashing, SQL shape of the statement query                                                      | Nothing                 |
| Integration | Duplicates, out-of-order versions, concurrent submissions, producer conflicts, window boundaries, multi-currency, health, metrics, correlation IDs | Docker (Testcontainers) |
| Property    | Random activity histories, duplicated, shuffled and sent concurrently, must match an independent reference model exactly                          | Docker (Testcontainers) |

Integration tests start a throwaway SQL Server container and run the real API in-process. Correctness under concurrency depends on database behaviour, so it is tested against the real engine, not a fake. To run only the unit tests: `dotnet test --filter "FullyQualifiedName~Unit"`.

The property test runs five fixed seeds. To explore more, or replay a failure it reports:

```bash
IHUB_PROPERTY_SEEDS=random:50 dotnet test --filter "FullyQualifiedName~ConvergencePropertyTests"
IHUB_PROPERTY_SEEDS=303       dotnet test --filter "FullyQualifiedName~ConvergencePropertyTests"
```

## Load and failure simulation

With the stack running (`docker compose up --build`), the simulator plays many at-least-once producers against it. It sends duplicated, out-of-order revisions concurrently, can restart the database container mid-run, and then checks every broker's statement against the reference model. It exits non-zero on any mismatch.

```bash
# Throughput: as fast as the service accepts
dotnet run --project tools/IHub.Simulator -c Release -- --seed 42

# Failure: steady 400/s, SQL Server container restarted after 15 s
dotnet run --project tools/IHub.Simulator -c Release -- --seed 7 --activities 6000 --rate 400 --restart-db-after 15
```

`--help` lists every option (`--url` if the API is not on port 8080, `--report <file>` to save the Markdown report). CI runs the failure scenario on every push.

## Configuration

All settings live in [appsettings.json](src/IHub.Api/appsettings.json) and can be overridden with environment variables (`Section__Key`).

| Setting                              | Default                      | Purpose                                           |
| ------------------------------------ | ---------------------------- | ------------------------------------------------- |
| `ConnectionStrings__IHub`            | (none)                       | SQL Server connection string                      |
| `Database__ApplyMigrationsOnStartup` | `false` (`true` locally)     | Apply EF Core migrations before accepting traffic |
| `Api__EnableApiReference`            | `false` (`true` locally)     | Serve `/scalar` and `/openapi/v1.json`            |
| `Ingestion__MaxBatchSize`            | `500`                        | Maximum activities per request                    |
| `Ingestion__MaxClockSkew`            | `00:05:00`                   | How far in the future `occurredAt` may be         |
| `Ingestion__SupportedCurrencies`     | ZAR, NAD, BWP, USD, EUR, GBP | ISO-4217 allow-list                               |
| `Ingestion__KnownSourceSystems`      | life-admin, investments, ... | Source systems tagged individually in metrics     |
| `Statements__MaxWindow`              | `366.00:00:00`               | Longest statement window per request              |

For local use, ports and the SQL Server password can be overridden with a `.env` file. See [.env.example](.env.example).

## Repository layout

```
src/IHub.Api/
  Domain/              Activity (stored state), ActivityRevision (validated input), status, shared limits
  Features/Activities/ POST /activities: contract, validator, idempotent ingestor, endpoint
  Features/Statements/ GET statement: contract, validator, query, endpoint
  Persistence/         DbContext, schema configuration, migrations, startup migrator
  Observability/       Correlation IDs, request logging, metrics, exception handling, health output
  Common/              Strict timestamp parsing, identifier rules
tests/IHub.Api.Tests/
  Unit/                No external dependencies
  Integration/         Real API + real SQL Server (Testcontainers), including the property test
tools/IHub.Simulator/
  Workload/            Seeded workload generator and reference model, shared with the property test
                       Load and failure simulator with statement reconciliation
IHub.http              Demo scenarios
docker-compose.yml     SQL Server + API
```

## Troubleshooting

| Symptom                                   | Fix                                                                                                                          |
| ----------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `port is already allocated` (1433 / 8080) | Create `.env` from `.env.example` and change `SQL_PORT` / `API_PORT`                                                         |
| `bind: ... forbidden by its access permissions` (Windows) | Windows has reserved the port (`netsh interface ipv4 show excludedportrange protocol=tcp`). Set `API_PORT=8088` in `.env` |
| SQL Server container exits immediately    | Give Docker at least 2 GB of memory; SQL Server will not start with less                                                     |
| Slow start on Apple Silicon               | The SQL Server image is x64-only and runs under Rosetta emulation in Docker Desktop. It works, but the first start is slower |
| `dotnet run` cannot connect               | Start the database first: `docker compose up -d sqlserver`, and wait for it to report `healthy` (`docker compose ps`)        |
