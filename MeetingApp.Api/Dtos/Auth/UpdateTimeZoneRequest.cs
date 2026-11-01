using System.ComponentModel.DataAnnotations;
using MeetingApp.Api.Validation;

namespace MeetingApp.Api.Dtos.Auth;

public class UpdateTimeZoneRequest
{
    [StringLength(100)]
    [IanaTimeZone]
    public string? TimeZoneId { get; set; }
}
