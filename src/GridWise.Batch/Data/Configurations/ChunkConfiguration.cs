using GridWise.Batch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GridWise.Batch.Data.Configurations;

public class ChunkConfiguration : IEntityTypeConfiguration<Chunk>
{
    public void Configure(EntityTypeBuilder<Chunk> builder)
    {
        builder.ToTable("chunks");
        builder.HasKey(c => new { c.JobId, c.ChunkNo });

        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Job>()
            .WithMany()
            .HasForeignKey(c => c.JobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
