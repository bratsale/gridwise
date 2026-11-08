using GridWise.Batch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GridWise.Batch.Data.Configurations;

public class JobResultConfiguration : IEntityTypeConfiguration<JobResult>
{
    public void Configure(EntityTypeBuilder<JobResult> builder)
    {
        builder.ToTable("job_results");
        builder.HasKey(r => new { r.JobId, r.Line });

        builder.Property(r => r.CustomerId).HasMaxLength(100).IsRequired();
        builder.Property(r => r.PlanName).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Total).HasPrecision(14, 2);

        builder.HasOne<Job>()
            .WithMany()
            .HasForeignKey(r => r.JobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
