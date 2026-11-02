using GridWise.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GridWise.Catalog.Data.Configurations;

public class PlanVersionConfiguration : IEntityTypeConfiguration<PlanVersion>
{
    public void Configure(EntityTypeBuilder<PlanVersion> builder)
    {
        builder.ToTable("plan_versions", t =>
            t.HasCheckConstraint("ck_plan_versions_discount", "discount >= 0 AND discount <= 1"));

        builder.HasKey(v => new { v.PlanId, v.VersionNo });

        builder.Property(v => v.Name).HasMaxLength(100).IsRequired();
        builder.Property(v => v.Discount).HasPrecision(5, 4);
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Plan>()
            .WithMany(p => p.Versions)
            .HasForeignKey(v => v.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
