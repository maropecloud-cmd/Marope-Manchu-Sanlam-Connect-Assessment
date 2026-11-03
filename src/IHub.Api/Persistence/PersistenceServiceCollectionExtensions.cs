using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace IHub.Api.Persistence;

internal static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContextPool<HubDbContext>((provider, options) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var database = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            var connectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string 'ConnectionStrings:{DatabaseOptions.ConnectionStringName}' is not configured.");
            }

            options.UseSqlServer(connectionString, sql =>
            {
                // Safe to retry: writes are idempotent.
                sql.EnableRetryOnFailure(database.MaxRetryCount, database.MaxRetryDelay, errorNumbersToAdd: null);
                sql.CommandTimeout(database.CommandTimeoutSeconds);
            });

            // Unique-key violations are normal for duplicates; real failures are logged by GlobalExceptionHandler.
            options.ConfigureWarnings(warnings => warnings.Log(
                (RelationalEventId.CommandError, LogLevel.Debug),
                (CoreEventId.SaveChangesFailed, LogLevel.Debug)));
        });

        services.AddHostedService<DatabaseMigrationService>();

        return services;
    }
}
