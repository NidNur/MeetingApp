using System.Diagnostics.CodeAnalysis;

namespace MeetingApp.Api.Services;

public static class IanaTimeZone
{
    public static bool TryResolve(string? timeZoneId, [NotNullWhen(true)] out TimeZoneInfo? timeZone)
    {
        timeZone = null;
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > 100)
            return false;

        var id = timeZoneId.Trim();
        if (id == "UTC")
        {
            timeZone = TimeZoneInfo.Utc;
            return true;
        }

        try
        {
            var resolved = TimeZoneInfo.FindSystemTimeZoneById(id);
            if (!resolved.HasIanaId || !StringComparer.Ordinal.Equals(resolved.Id, id))
                return false;

            timeZone = resolved;
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
