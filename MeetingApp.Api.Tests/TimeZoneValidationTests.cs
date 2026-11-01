using System.ComponentModel.DataAnnotations;
using MeetingApp.Api.Dtos.Auth;
using MeetingApp.Api.Services;

namespace MeetingApp.Api.Tests;

public class TimeZoneValidationTests
{
    [Fact]
    public void Wrong_casing_is_rejected_even_after_the_canonical_identifier_is_cached()
    {
        Assert.True(IanaTimeZone.TryResolve("Europe/Rome", out _));
        Assert.False(IanaTimeZone.TryResolve("europe/rome", out _));
    }

    [Theory]
    [InlineData("Europe/Istanbul")]
    [InlineData("Europe/Rome")]
    [InlineData("Asia/Kathmandu")]
    [InlineData("UTC")]
    public void Resolves_valid_IANA_identifiers(string timeZoneId)
    {
        Assert.True(IanaTimeZone.TryResolve(timeZoneId, out var timeZone));
        Assert.NotNull(timeZone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("No/Such_Zone")]
    [InlineData("Turkey Standard Time")]
    [InlineData("W. Europe Standard Time")]
    public void Does_not_resolve_missing_invalid_or_Windows_identifiers(string? timeZoneId)
    {
        Assert.False(IanaTimeZone.TryResolve(timeZoneId, out var timeZone));
        Assert.Null(timeZone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Europe/Rome")]
    [InlineData(" Europe/Istanbul ")]
    [InlineData("UTC")]
    public void All_user_requests_accept_an_optional_or_valid_time_zone(string? timeZoneId)
    {
        foreach (var request in CreateRequests(timeZoneId))
        {
            var errors = Validate(request);
            Assert.Empty(errors);
        }
    }

    [Theory]
    [InlineData("No/Such_Zone")]
    [InlineData("Turkey Standard Time")]
    [InlineData("W. Europe Standard Time")]
    [InlineData("+03:00")]
    [InlineData("<script>")]
    public void All_user_requests_reject_invalid_time_zone_values(string timeZoneId)
    {
        foreach (var request in CreateRequests(timeZoneId))
        {
            Assert.Contains(Validate(request), error => error.MemberNames.Contains("TimeZoneId"));
        }
    }

    [Fact]
    public void All_user_requests_reject_a_time_zone_longer_than_the_database_limit()
    {
        foreach (var request in CreateRequests(new string('x', 101)))
        {
            Assert.Contains(Validate(request), error => error.MemberNames.Contains("TimeZoneId"));
        }
    }

    private static object[] CreateRequests(string? timeZoneId) =>
    [
        new RegisterRequest
        {
            FirstName = "Test", LastName = "User", Email = "timezone@example.test",
            PhoneNumber = "+905321234567", Password = "TimeZoneTest2026!", TimeZoneId = timeZoneId
        },
        new LoginRequest
        {
            Email = "timezone@example.test", Password = "TimeZoneTest2026!", TimeZoneId = timeZoneId
        },
        new UpdateTimeZoneRequest { TimeZoneId = timeZoneId }
    ];

    private static List<ValidationResult> Validate(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }
}
