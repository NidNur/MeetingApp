using MeetingApp.Model.Entities;
using Microsoft.EntityFrameworkCore;

namespace MeetingApp.Context;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<Meeting> Meetings => Set<Meeting>();

    public DbSet<MeetingDeletionLog> MeetingDeletionLogs => Set<MeetingDeletionLog>();

    public DbSet<EmailOutboxMessage> EmailOutboxMessages => Set<EmailOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly);
    }
}