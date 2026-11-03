using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IHub.Api.Persistence;

internal sealed partial class DatabaseMigrationService(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseMigrationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.ApplyMigrationsOnStartup)
        {
            LogMigrationsSkipped(logger);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        await db.Database.MigrateAsync(cancellationToken);

        LogMigrationsApplied(logger, pending.Count, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Database migrations skipped (Database:ApplyMigrationsOnStartup is false)")]
    private static partial void LogMigrationsSkipped(ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "Database migrations applied: {PendingMigrationCount} pending migration(s) in {ElapsedMs:0} ms")]
    private static partial void LogMigrationsApplied(ILogger logger, int pendingMigrationCount, double elapsedMs);
}
