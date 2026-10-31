namespace MeetingApp.Model.Entities;

public class EmailOutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string RecipientEmail { get; set; } = string.Empty;

    public string RecipientName { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string TextBody { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? SentAtUtc { get; set; }

    public int AttemptCount { get; set; }

    public Guid? LockId { get; set; }

    public DateTime? LockedUntilUtc { get; set; }

    public string? LastError { get; set; }
}
