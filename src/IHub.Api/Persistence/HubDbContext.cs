using IHub.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace IHub.Api.Persistence;

public sealed class HubDbContext(DbContextOptions<HubDbContext> options) : DbContext(options)
{
    public DbSet<Activity> Activities => Set<Activity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HubDbContext).Assembly);
}
