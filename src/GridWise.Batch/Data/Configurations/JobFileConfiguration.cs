using GridWise.Batch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GridWise.Batch.Data.Configurations;

public class JobFileConfiguration : IEntityTypeConfiguration<JobFile>
{
    public void Configure(EntityTypeBuilder<JobFile> builder)
    {
        builder.ToTable("job_files");
        builder.HasKey(f => f.JobId);

        builder.Property(f => f.Content).HasColumnType("bytea").IsRequired();

        builder.HasOne<Job>()
            .WithOne()
            .HasForeignKey<JobFile>(f => f.JobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
