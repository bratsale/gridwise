using GridWise.Batch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GridWise.Batch.Data.Configurations;

public class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.FileName).HasMaxLength(255).IsRequired();

        builder.HasIndex(j => j.CreatedAt);
    }
}
