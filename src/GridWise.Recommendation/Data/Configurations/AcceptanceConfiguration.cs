using GridWise.Recommendation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GridWise.Recommendation.Data.Configurations;

public class AcceptanceConfiguration : IEntityTypeConfiguration<Acceptance>
{
    public void Configure(EntityTypeBuilder<Acceptance> builder)
    {
        builder.ToTable("acceptances");
        builder.HasKey(a => a.RecommendationId);

        builder.HasOne<PlanRecommendation>()
            .WithOne()
            .HasForeignKey<Acceptance>(a => a.RecommendationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
