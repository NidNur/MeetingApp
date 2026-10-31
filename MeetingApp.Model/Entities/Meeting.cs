namespace MeetingApp.Model.Entities;

public class Meeting
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime StartsAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    public string? DocumentPath { get; set; }

    public string? DocumentOriginalName { get; set; }

    public bool IsCancelled { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int CreatedById { get; set; }

    public AppUser CreatedBy { get; set; } = null!;
}