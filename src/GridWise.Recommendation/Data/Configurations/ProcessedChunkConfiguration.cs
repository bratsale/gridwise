using GridWise.Recommendation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GridWise.Recommendation.Data.Configurations;

public class ProcessedChunkConfiguration : IEntityTypeConfiguration<ProcessedChunk>
{
    public void Configure(EntityTypeBuilder<ProcessedChunk> builder)
    {
        builder.ToTable("processed_chunks");
        builder.HasKey(c => new { c.JobId, c.ChunkNo });
    }
}
