using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IHub.Api.Persistence;

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HubDbContext>
{
    public HubDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseSqlServer("Server=design-time-only;Database=IHub")
            .Options;

        return new HubDbContext(options);
    }
}
