using System.ComponentModel.DataAnnotations;
using MeetingApp.Api.Validation;

namespace MeetingApp.Api.Dtos.Auth;

public class LoginRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string Password { get; set; } = string.Empty;

    [StringLength(100)]
    [IanaTimeZone]
    public string? TimeZoneId { get; set; }
}
