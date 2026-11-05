using GridWise.Recommendation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GridWise.Recommendation.Data.Configurations;

public class PlanRecommendationConfiguration : IEntityTypeConfiguration<PlanRecommendation>
{
    public void Configure(EntityTypeBuilder<PlanRecommendation> builder)
    {
        builder.ToTable("recommendations");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ConsumptionKwh).HasPrecision(12, 3);
        builder.Property(r => r.TaxGroupCode).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Items).HasColumnType("jsonb");
    }
}
