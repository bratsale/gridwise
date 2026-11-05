using GridWise.Recommendation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GridWise.Recommendation.Data.Configurations;

public class PlanVersionCopyConfiguration : IEntityTypeConfiguration<PlanVersionCopy>
{
    public void Configure(EntityTypeBuilder<PlanVersionCopy> builder)
    {
        builder.ToTable("plan_versions");
        builder.HasKey(v => new { v.PlanId, v.VersionNo });

        builder.Property(v => v.Name).HasMaxLength(100).IsRequired();
        builder.Property(v => v.Discount).HasPrecision(5, 4);
        builder.Property(v => v.Status).HasMaxLength(20).IsRequired();
        builder.Property(v => v.Tiers).HasColumnType("jsonb");
    }
}
