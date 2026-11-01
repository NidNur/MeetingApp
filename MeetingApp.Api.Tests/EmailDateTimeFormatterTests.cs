using MeetingApp.Api.Services;

namespace MeetingApp.Api.Tests;

public class EmailDateTimeFormatterTests
{
    [Theory]
    [InlineData(11, 1, "Europe/Istanbul", "01.11.2026 14:00 (Europe/Istanbul, UTC+03:00)")]
    [InlineData(11, 1, "Europe/Rome", "01.11.2026 12:00 (Europe/Rome, UTC+01:00)")]
    [InlineData(7, 1, "Europe/Istanbul", "01.07.2026 14:00 (Europe/Istanbul, UTC+03:00)")]
    [InlineData(7, 1, "Europe/Rome", "01.07.2026 13:00 (Europe/Rome, UTC+02:00)")]
    [InlineData(11, 1, "Asia/Kathmandu", "01.11.2026 16:45 (Asia/Kathmandu, UTC+05:45)")]
    [InlineData(11, 1, "America/New_York", "01.11.2026 06:00 (America/New_York, UTC-05:00)")]
    public void Formats_recipient_time_using_the_offset_at_the_meeting_date(
        int month, int day, string timeZoneId, string expected)
    {
        var instant = new DateTime(2026, month, day, 11, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expected, EmailDateTimeFormatter.Format(instant, timeZoneId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not/A_Real_Zone")]
    [InlineData("Turkey Standard Time")]
    [InlineData("<script>")]
    public void Missing_or_invalid_recipient_time_zone_preserves_the_existing_UTC_format(string? timeZoneId)
    {
        var instant = new DateTime(2026, 11, 1, 11, 0, 0, DateTimeKind.Utc);

        Assert.Equal("01.11.2026 11:00 UTC", EmailDateTimeFormatter.Format(instant, timeZoneId));
    }

    [Fact]
    public void SQL_datetime2_without_DateTime_kind_is_interpreted_as_stored_UTC()
    {
        var sqlValue = new DateTime(2026, 11, 1, 11, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal("01.11.2026 12:00 (Europe/Rome, UTC+01:00)",
            EmailDateTimeFormatter.Format(sqlValue, "Europe/Rome"));
    }

    [Fact]
    public void Explicit_UTC_preference_is_supported()
    {
        var instant = new DateTime(2026, 11, 1, 11, 0, 0, DateTimeKind.Utc);

        Assert.Equal("01.11.2026 11:00 (UTC, UTC+00:00)",
            EmailDateTimeFormatter.Format(instant, "UTC"));
    }

    [Fact]
    public void Whitespace_around_a_valid_identifier_is_removed_from_the_display()
    {
        var instant = new DateTime(2026, 11, 1, 11, 0, 0, DateTimeKind.Utc);

        Assert.Equal("01.11.2026 12:00 (Europe/Rome, UTC+01:00)",
            EmailDateTimeFormatter.Format(instant, " Europe/Rome "));
    }

    [Fact]
    public void Overlong_identifier_falls_back_without_throwing()
    {
        var instant = new DateTime(2026, 11, 1, 11, 0, 0, DateTimeKind.Utc);

        Assert.Equal("01.11.2026 11:00 UTC", EmailDateTimeFormatter.Format(instant, new string('x', 101)));
    }
}
