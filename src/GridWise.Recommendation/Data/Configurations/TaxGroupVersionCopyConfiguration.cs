using GridWise.Recommendation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GridWise.Recommendation.Data.Configurations;

public class TaxGroupVersionCopyConfiguration : IEntityTypeConfiguration<TaxGroupVersionCopy>
{
    public void Configure(EntityTypeBuilder<TaxGroupVersionCopy> builder)
    {
        builder.ToTable("tax_group_versions");
        builder.HasKey(v => new { v.Code, v.VersionNo });

        builder.Property(v => v.Code).HasMaxLength(50);
        builder.Property(v => v.Vat).HasPrecision(5, 4);
        builder.Property(v => v.EcoTax).HasPrecision(12, 4);
        builder.Property(v => v.Status).HasMaxLength(20).IsRequired();
    }
}
