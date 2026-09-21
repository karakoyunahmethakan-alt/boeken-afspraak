using System.Globalization;
using System.Text;

namespace BoekenAfspraak.Api.Services;

public static class IcsBuilder
{
    // Builds a single-event .ics file. status: "CONFIRMED" or "CANCELLED".
    // A CANCELLED .ics with the same Uid tells a calendar app to remove/void
    // the event it previously added for this appointment.
    public static string Build(
        Guid uid,
        string summary,
        string description,
        DateOnly date,
        string timeSlot,
        TimeZoneInfo tz,
        int durationMinutes,
        string status,
        int sequence)
    {
        var (hour, minute) = ParseSlot(timeSlot);
        var localStart = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0, DateTimeKind.Unspecified);
        var utcStart = TimeZoneInfo.ConvertTimeToUtc(localStart, tz);
        var utcEnd = utcStart.AddMinutes(durationMinutes);
        var nowUtc = DateTime.UtcNow;

        string Fmt(DateTime dt) => dt.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");

        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\n");
        sb.Append("VERSION:2.0\r\n");
        sb.Append("PRODID:-//BoekenAfspraak//NL\r\n");
        sb.Append("CALSCALE:GREGORIAN\r\n");
        sb.Append("METHOD:").Append(status == "CANCELLED" ? "CANCEL" : "PUBLISH").Append("\r\n");
        sb.Append("BEGIN:VEVENT\r\n");
        sb.Append("UID:").Append(uid).Append("@boeken-afspraak\r\n");
        sb.Append("DTSTAMP:").Append(Fmt(nowUtc)).Append("\r\n");
        sb.Append("DTSTART:").Append(Fmt(utcStart)).Append("\r\n");
        sb.Append("DTEND:").Append(Fmt(utcEnd)).Append("\r\n");
        sb.Append("SUMMARY:").Append(Escape(summary)).Append("\r\n");
        sb.Append("DESCRIPTION:").Append(Escape(description)).Append("\r\n");
        sb.Append("SEQUENCE:").Append(sequence).Append("\r\n");
        sb.Append("STATUS:").Append(status).Append("\r\n");
        sb.Append("END:VEVENT\r\n");
        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }

    private static (int hour, int minute) ParseSlot(string timeSlot)
    {
        var parts = timeSlot.Split(':');
        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }
}
