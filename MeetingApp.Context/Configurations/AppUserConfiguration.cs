using MeetingApp.Model.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeetingApp.Context.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(user => user.Id);

        builder.Property(user => user.FirstName)
            .HasMaxLength(100).IsRequired();

        builder.Property(user => user.LastName)
            .HasMaxLength(100).IsRequired();

        builder.Property(user => user.Email)
            .HasMaxLength(256).IsRequired();

        builder.Property(user => user.NormalizedEmail)
            .HasMaxLength(256).IsRequired();

        builder.HasIndex(user => user.NormalizedEmail)
            .IsUnique();

        builder.Property(user => user.PhoneNumber)
            .HasMaxLength(30).IsRequired();

        builder.Property(user => user.PasswordHash)
            .HasMaxLength(1024).IsRequired();

        builder.Property(user => user.ProfileImagePath)
            .HasMaxLength(500);
    }
}