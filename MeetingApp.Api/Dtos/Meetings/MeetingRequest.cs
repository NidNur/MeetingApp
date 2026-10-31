using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace MeetingApp.Api.Dtos.Meetings;

public class MeetingRequest : IValidatableObject
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public DateTimeOffset? StartsAtUtc { get; set; }

    [Required]
    public DateTimeOffset? EndsAtUtc { get; set; }

    public IFormFile? Document { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartsAtUtc.HasValue && EndsAtUtc.HasValue
            && EndsAtUtc.Value <= StartsAtUtc.Value)
        {
            yield return new ValidationResult(
                "Bitiş tarihi başlangıç tarihinden sonra olmalıdır.",
                [nameof(EndsAtUtc)]);
        }
    }
}
