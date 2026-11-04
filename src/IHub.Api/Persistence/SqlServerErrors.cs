using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IHub.Api.Persistence;

internal static class SqlServerErrors
{
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;
    private const byte FatalSeverity = 20;

    // Raised while the server is unreachable, restarting, failing over or throttling.
    private static readonly HashSet<int> UnavailableErrorNumbers =
        [-2, 20, 53, 64, 121, 233, 4060, 6005, 10053, 10054, 10060, 10061, 40197, 40501, 40613];

    public static bool IsUniqueKeyViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: UniqueConstraintViolation or UniqueIndexViolation };

    public static bool IsDatabaseUnavailable(Exception exception) => exception switch
    {
        RetryLimitExceededException or TimeoutException => true,
        SqlException sql => sql.Class >= FatalSeverity
            || sql.Errors.Cast<SqlError>().Any(error => UnavailableErrorNumbers.Contains(error.Number)),
        DbUpdateException { InnerException: { } inner } => IsDatabaseUnavailable(inner),
        _ => false,
    };
}
