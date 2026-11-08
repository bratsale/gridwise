using GridWise.Batch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GridWise.Batch.Data.Configurations;

public class JobErrorConfiguration : IEntityTypeConfiguration<JobError>
{
    public void Configure(EntityTypeBuilder<JobError> builder)
    {
        builder.ToTable("job_errors");
        builder.HasKey(e => new { e.JobId, e.Line });

        builder.Property(e => e.Error).HasMaxLength(1000).IsRequired();

        builder.HasOne<Job>()
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
