namespace MeetingApp.Model.Entities;

public class AppUser
{
    public int Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string NormalizedEmail { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string? TimeZoneId { get; set; }

    public string PasswordHash { get; set; } = string.Empty;

    public string? ProfileImagePath { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
