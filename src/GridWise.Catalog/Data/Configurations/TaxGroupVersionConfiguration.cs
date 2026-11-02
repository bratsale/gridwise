using GridWise.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GridWise.Catalog.Data.Configurations;

public class TaxGroupVersionConfiguration : IEntityTypeConfiguration<TaxGroupVersion>
{
    public void Configure(EntityTypeBuilder<TaxGroupVersion> builder)
    {
        builder.ToTable("tax_group_versions", t =>
        {
            t.HasCheckConstraint("ck_tax_group_versions_vat", "vat >= 0 AND vat <= 1");
            t.HasCheckConstraint("ck_tax_group_versions_eco_tax", "eco_tax >= 0");
        });

        builder.HasKey(v => new { v.Code, v.VersionNo });

        builder.Property(v => v.Code).HasMaxLength(50);
        builder.Property(v => v.Vat).HasPrecision(5, 4);
        builder.Property(v => v.EcoTax).HasPrecision(12, 4);
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<TaxGroup>()
            .WithMany(g => g.Versions)
            .HasForeignKey(v => v.Code)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
