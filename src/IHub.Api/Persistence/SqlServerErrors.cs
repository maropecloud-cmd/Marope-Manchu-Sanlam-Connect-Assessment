using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace IHub.Api.Persistence;

internal static class SqlServerErrors
{
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    public static bool IsUniqueKeyViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: UniqueConstraintViolation or UniqueIndexViolation };
}
