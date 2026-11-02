using GridWise.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GridWise.Catalog.Data.Configurations;

public class PlanTierConfiguration : IEntityTypeConfiguration<PlanTier>
{
    public void Configure(EntityTypeBuilder<PlanTier> builder)
    {
        builder.ToTable("plan_tiers", t =>
            t.HasCheckConstraint("ck_plan_tiers_price_per_kwh", "price_per_kwh >= 0"));

        builder.HasKey(t => new { t.PlanId, t.VersionNo, t.TierNo });

        builder.Property(t => t.Threshold).HasPrecision(12, 3);
        builder.Property(t => t.PricePerKwh).HasPrecision(12, 4);

        builder.HasOne<PlanVersion>()
            .WithMany(v => v.Tiers)
            .HasForeignKey(t => new { t.PlanId, t.VersionNo })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
