using System.ComponentModel.DataAnnotations;

namespace IHub.Api.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public const string ConnectionStringName = "IHub";

    public bool ApplyMigrationsOnStartup { get; set; }

    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    [Range(0, 10)]
    public int MaxRetryCount { get; set; } = 5;

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(10);
}
