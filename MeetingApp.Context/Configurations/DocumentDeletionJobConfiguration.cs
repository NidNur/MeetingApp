using MeetingApp.Model.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeetingApp.Context.Configurations;

public class DocumentDeletionJobConfiguration : IEntityTypeConfiguration<DocumentDeletionJob>
{
    public void Configure(EntityTypeBuilder<DocumentDeletionJob> builder)
    {
        builder.ToTable("DocumentDeletionJobs");
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).ValueGeneratedNever();
        builder.Property(job => job.DocumentPath).HasMaxLength(500).IsRequired();
        builder.HasIndex(job => new { job.NextAttemptAtUtc, job.Id });
    }
}
