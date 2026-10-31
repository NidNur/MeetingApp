namespace MeetingApp.Api.Dtos.Meetings;

public class MeetingResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime StartsAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public bool IsCancelled { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public string? DocumentOriginalName { get; set; }

    public bool HasDocument { get; set; }
}
