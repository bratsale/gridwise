using GridWise.Recommendation.Domain;
using Microsoft.EntityFrameworkCore;

namespace GridWise.Recommendation.Data;

public class RecommendationDbContext(DbContextOptions<RecommendationDbContext> options) : DbContext(options)
{
    public DbSet<PlanVersionCopy> PlanVersions => Set<PlanVersionCopy>();
    public DbSet<TaxGroupVersionCopy> TaxGroupVersions => Set<TaxGroupVersionCopy>();
    public DbSet<PlanRecommendation> PlanRecommendations => Set<PlanRecommendation>();
    public DbSet<Acceptance> Acceptances => Set<Acceptance>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(RecommendationDbContext).Assembly);
}
