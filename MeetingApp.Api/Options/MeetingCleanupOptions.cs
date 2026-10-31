using System.ComponentModel.DataAnnotations;

namespace MeetingApp.Api.Options;

public class MeetingCleanupOptions
{
    public bool Enabled { get; set; } = true;

    [Range(1, 86400)]
    public int PollIntervalSeconds { get; set; } = 60;

    [Range(1, 8760)]
    public int RetentionHours { get; set; } = 24;

    [Range(1, 1000)]
    public int BatchSize { get; set; } = 100;
}
