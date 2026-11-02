using GridWise.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GridWise.Catalog.Data.Configurations;

public class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("idempotency_keys");
        builder.HasKey(k => new { k.Key, k.Endpoint });

        builder.Property(k => k.Key).HasMaxLength(100);
        builder.Property(k => k.Endpoint).HasMaxLength(100);
        builder.Property(k => k.RequestHash).HasMaxLength(64);
        builder.Property(k => k.ResponseBody).HasColumnType("jsonb");
    }
}
