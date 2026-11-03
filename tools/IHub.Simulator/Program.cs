using System.Text;
using IHub.Simulator;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(SimulatorOptions.Usage);
    return 0;
}

SimulatorOptions options;
try
{
    options = SimulatorOptions.Parse(args);
}
catch (Exception exception) when (exception is ArgumentException or FormatException)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine(SimulatorOptions.Usage);
    return 2;
}

Console.OutputEncoding = Encoding.UTF8;
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

using var http = new HttpClient { BaseAddress = options.BaseUrl, Timeout = TimeSpan.FromSeconds(60) };

try
{
    using var ready = await http.GetAsync(new Uri("/health/ready", UriKind.Relative), cancellation.Token);
    if (!ready.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"{options.BaseUrl} is not ready ({(int)ready.StatusCode}). Start it with: docker compose up --build");
        return 2;
    }
}
catch (HttpRequestException exception)
{
    Console.Error.WriteLine($"Cannot reach {options.BaseUrl}: {exception.Message}. Start it with: docker compose up --build");
    return 2;
}

var result = await new LoadRunner(options, http).RunAsync(cancellation.Token);
var report = Report.Render(result);

Console.WriteLine();
Console.WriteLine(report);

if (options.ReportPath is { } path)
{
    await File.WriteAllTextAsync(path, report, cancellation.Token);
    Console.WriteLine($"Report written to {Path.GetFullPath(path)}");
}

return result.Passed ? 0 : 1;
