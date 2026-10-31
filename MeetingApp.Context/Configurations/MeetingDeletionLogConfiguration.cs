using MeetingApp.Model.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeetingApp.Context.Configurations;

public class MeetingDeletionLogConfiguration : IEntityTypeConfiguration<MeetingDeletionLog>
{
    public void Configure(EntityTypeBuilder<MeetingDeletionLog> builder)
    {
        builder.ToTable("MeetingDeletionLogs");

        builder.HasKey(log => log.Id);

        builder.Property(log => log.Name)
            .HasMaxLength(200).IsRequired();

        builder.Property(log => log.Description)
            .HasMaxLength(4000).IsRequired();

        builder.Property(log => log.DocumentPath)
            .HasMaxLength(500);

        builder.Property(log => log.DocumentOriginalName)
            .HasMaxLength(255);

        builder.Property(log => log.DeletedAtUtc)
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(log => log.MeetingId);
    }
}
