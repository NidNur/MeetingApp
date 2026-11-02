namespace MeetingApp.Model.Entities;

public class DocumentDeletionJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string DocumentPath { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime NextAttemptAtUtc { get; set; }

    public int AttemptCount { get; set; }
}
