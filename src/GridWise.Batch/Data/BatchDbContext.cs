using GridWise.Batch.Domain;
using Microsoft.EntityFrameworkCore;

namespace GridWise.Batch.Data;

public class BatchDbContext(DbContextOptions<BatchDbContext> options) : DbContext(options)
{
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobFile> JobFiles => Set<JobFile>();
    public DbSet<Chunk> Chunks => Set<Chunk>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(BatchDbContext).Assembly);
}
