using System.Data.Common;
using IHub.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IHub.Api.Tests.Unit;

public sealed class SqlServerErrorsTests
{
    [Fact]
    public void Exhausted_retries_and_timeouts_mean_the_database_is_unavailable()
    {
        Assert.True(SqlServerErrors.IsDatabaseUnavailable(new RetryLimitExceededException("retries exhausted")));
        Assert.True(SqlServerErrors.IsDatabaseUnavailable(new TimeoutException()));
    }

    [Fact]
    public void Other_database_errors_are_not_reported_as_unavailable()
    {
        var error = new FakeDbException("Invalid column name 'Amout'.");

        Assert.False(SqlServerErrors.IsDatabaseUnavailable(error));
        Assert.False(SqlServerErrors.IsDatabaseUnavailable(new DbUpdateException("update failed", error)));
        Assert.False(SqlServerErrors.IsDatabaseUnavailable(new InvalidOperationException()));
    }

    private sealed class FakeDbException(string message) : DbException(message);
}
