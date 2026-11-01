using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using MeetingApp.Api.Validation;

namespace MeetingApp.Api.Dtos.Auth;

public class RegisterRequest
{
    [Required]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [Phone]
    [StringLength(30)]
    public string PhoneNumber { get; set; } = string.Empty;

    [StringLength(100)]
    [IanaTimeZone]
    public string? TimeZoneId { get; set; }

    [Required]
    [StringLength(128, MinimumLength = 12)]
    public string Password { get; set; } = string.Empty;

    public IFormFile? ProfileImage { get; set; }
}
