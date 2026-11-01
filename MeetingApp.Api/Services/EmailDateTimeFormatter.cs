using System.Globalization;

namespace MeetingApp.Api.Services;

public static class EmailDateTimeFormatter
{
    public static string Format(DateTime utc, string? timeZoneId)
    {
        // SQL datetime2 does not retain DateTime.Kind; these values are stored as UTC.
        var instant = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        if (!IanaTimeZone.TryResolve(timeZoneId, out var timeZone))
            return instant.ToString("dd.MM.yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);

        var local = TimeZoneInfo.ConvertTime(new DateTimeOffset(instant), timeZone);
        var offset = local.Offset;
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var offsetText = offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        var dateText = local.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

        return $"{dateText} ({timeZoneId!.Trim()}, UTC{sign}{offsetText})";
    }
}
