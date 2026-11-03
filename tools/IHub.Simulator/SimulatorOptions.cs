using System.Globalization;

namespace IHub.Simulator;

internal sealed record SimulatorOptions
{
    public Uri BaseUrl { get; init; } = new("http://localhost:8080");

    public int Activities { get; init; } = 5_000;

    public int Intermediaries { get; init; } = 200;

    public int MaxVersions { get; init; } = 4;

    public double DuplicateRate { get; init; } = 0.3;

    public int BatchSize { get; init; } = 25;

    public int Concurrency { get; init; } = 16;

    public double Rate { get; init; }

    public int Seed { get; init; } = Environment.TickCount & int.MaxValue;

    public TimeSpan? RestartDatabaseAfter { get; init; }

    public string DatabaseContainer { get; init; } = "ihub-sqlserver-1";

    public TimeSpan RetryBudget { get; init; } = TimeSpan.FromMinutes(3);

    public string? ReportPath { get; init; }

    public const string Usage = """
        IHub load and failure simulator: drives the running API with an adversarial workload and reconciles every
        statement against an independent reference model.

        Usage: dotnet run --project tools/IHub.Simulator -- [options]

          --url <url>                  API base URL (default http://localhost:8080)
          --activities <n>             Distinct activities (default 5000)
          --intermediaries <n>         Intermediaries the activities are spread over (default 200)
          --max-versions <n>           Revisions per activity, 1..n (default 4)
          --duplicate-rate <0..1>      Chance each delivery is redelivered (default 0.3)
          --batch-size <n>             Activities per request (default 25)
          --concurrency <n>            Concurrent producers (default 16)
          --rate <n>                   Target activities/second; 0 = as fast as possible (default 0)
          --seed <n>                   Workload seed, for exact replay (default random)
          --restart-db-after <sec>     Restart the database container mid-run (chaos)
          --db-container <name>        Container to restart (default ihub-sqlserver-1)
          --report <path>              Also write the report as Markdown
        """;

    public static SimulatorOptions Parse(string[] args)
    {
        var options = new SimulatorOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i];
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {name}.");
            int Int() => int.Parse(Value(), CultureInfo.InvariantCulture);
            double Double() => double.Parse(Value(), CultureInfo.InvariantCulture);

            options = name switch
            {
                "--url" => options with { BaseUrl = new Uri(Value()) },
                "--activities" => options with { Activities = Int() },
                "--intermediaries" => options with { Intermediaries = Int() },
                "--max-versions" => options with { MaxVersions = Int() },
                "--duplicate-rate" => options with { DuplicateRate = Double() },
                "--batch-size" => options with { BatchSize = Int() },
                "--concurrency" => options with { Concurrency = Int() },
                "--rate" => options with { Rate = Double() },
                "--seed" => options with { Seed = Int() },
                "--restart-db-after" => options with { RestartDatabaseAfter = TimeSpan.FromSeconds(Double()) },
                "--db-container" => options with { DatabaseContainer = Value() },
                "--report" => options with { ReportPath = Value() },
                _ => throw new ArgumentException($"Unknown option '{name}'."),
            };
        }

        return options;
    }
}
