using System.ComponentModel.DataAnnotations;

namespace MeetingApp.Api.Options;

public class DatabaseStartupOptions
{
    public bool ApplyMigrations { get; set; }

    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 6;

    [Range(1, 60)]
    public int RetryDelaySeconds { get; set; } = 5;
}
