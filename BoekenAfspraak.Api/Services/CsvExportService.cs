using System.Globalization;
using System.Text;
using BoekenAfspraak.Api.Models;

namespace BoekenAfspraak.Api.Services;

public static class CsvExportService
{
    public static byte[] BuildAppointmentsCsv(IEnumerable<Appointment> appointments)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id;Datum;Tijd;Status;Naam;Adres;E-mail;Telefoon;AantalBoeken;Soort;GeschatePrijs;OorspronkelijkeDatum;OorspronkelijkeTijd;AantalKeerVerzet;Aangemaakt");

        string Esc(string? s) => (s ?? "").Replace(";", ",").Replace("\r", " ").Replace("\n", " ");

        foreach (var a in appointments)
        {
            sb.AppendLine(string.Join(";", new[]
            {
                a.Id.ToString(CultureInfo.InvariantCulture),
                a.Date.ToString("yyyy-MM-dd"),
                a.TimeSlot,
                a.Status.ToString(),
                Esc(a.Name),
                Esc(a.Address),
                Esc(a.Email),
                Esc(a.Phone),
                a.BookCount.ToString(CultureInfo.InvariantCulture),
                Esc(a.BookType),
                a.EstimatedPriceEuro.ToString("0.00", CultureInfo.InvariantCulture),
                a.OriginalDate?.ToString("yyyy-MM-dd") ?? "",
                a.OriginalTimeSlot ?? "",
                a.RescheduleCount.ToString(CultureInfo.InvariantCulture),
                a.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm")
            }));
        }

        // UTF-8 BOM so Excel opens accented characters correctly.
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }
}
