using MeetingApp.Model.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeetingApp.Context.Configurations;

public class MeetingConfiguration : IEntityTypeConfiguration<Meeting>
{
    public void Configure(EntityTypeBuilder<Meeting> builder)
    {
        builder.ToTable("Meetings", table =>
        {
            table.HasCheckConstraint(
                "CK_Meetings_DateRange",
                "[EndsAtUtc] > [StartsAtUtc]");

            table.UseSqlOutputClause(false);
        });

        builder.HasKey(meeting => meeting.Id);

        builder.Property(meeting => meeting.Name)
            .HasMaxLength(200).IsRequired();

        builder.Property(meeting => meeting.Description)
            .HasMaxLength(4000).IsRequired();

        builder.Property(meeting => meeting.DocumentPath)
            .HasMaxLength(500);

        builder.Property(meeting => meeting.DocumentOriginalName)
            .HasMaxLength(255);

        builder.HasOne(meeting => meeting.CreatedBy)
            .WithMany()
            .HasForeignKey(meeting => meeting.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(meeting => new
        {
            meeting.IsCancelled,
            meeting.CancelledAtUtc
        });
    }
}