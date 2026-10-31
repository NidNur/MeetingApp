using MeetingApp.Model.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeetingApp.Context.Configurations;

public class EmailOutboxMessageConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable("EmailOutboxMessages");

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();

        builder.Property(message => message.RecipientEmail).HasMaxLength(256).IsRequired();
        builder.Property(message => message.RecipientName).HasMaxLength(256).IsRequired();
        builder.Property(message => message.Subject).HasMaxLength(200).IsRequired();
        builder.Property(message => message.TextBody).HasMaxLength(10000).IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(500);

        builder.HasIndex(message => new { message.SentAtUtc, message.NextAttemptAtUtc });
    }
}
