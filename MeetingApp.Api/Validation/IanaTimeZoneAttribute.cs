using System.ComponentModel.DataAnnotations;
using MeetingApp.Api.Services;

namespace MeetingApp.Api.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class IanaTimeZoneAttribute : ValidationAttribute
{
    public IanaTimeZoneAttribute()
        : base("Geçerli bir IANA saat dilimi girin. Örnek: Europe/Istanbul.")
    {
    }

    public override bool IsValid(object? value) =>
        value is null || value is string text
            && (string.IsNullOrWhiteSpace(text) || IanaTimeZone.TryResolve(text, out _));
}
