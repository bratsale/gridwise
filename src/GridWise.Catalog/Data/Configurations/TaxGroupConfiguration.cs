using GridWise.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GridWise.Catalog.Data.Configurations;

public class TaxGroupConfiguration : IEntityTypeConfiguration<TaxGroup>
{
    public void Configure(EntityTypeBuilder<TaxGroup> builder)
    {
        builder.ToTable("tax_groups");
        builder.HasKey(g => g.Code);
        builder.Property(g => g.Code).HasMaxLength(50);
    }
}
