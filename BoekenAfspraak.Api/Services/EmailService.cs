using Microsoft.Extensions.Options;
using MimeKit;
using MailKit.Net.Smtp;
using BoekenAfspraak.Api.Models;

namespace BoekenAfspraak.Api.Services;

public class EmailService
{
    private readonly SmtpOptions _smtp;
    private readonly AppOptions _app;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<SmtpOptions> smtp, IOptions<AppOptions> app, ILogger<EmailService> logger)
    {
        _smtp = smtp.Value;
        _app = app.Value;
        _logger = logger;
    }

    public async Task SendOwnerNotificationAsync(Appointment a, string icsContent, string icsFileName)
    {
        var body = $"""
            Nieuwe afspraakaanvraag ontvangen.

            Naam: {a.Name}
            Adres: {a.Address}
            Telefoon: {a.Phone ?? "-"}
            E-mail: {a.Email}
            Aantal boeken: {a.BookCount}
            Soort boeken: {a.BookType ?? "-"}
            Geschatte prijsindicatie: € {a.EstimatedPriceEuro:0.00} ({(a.PricedByIsbn ? "op basis van ISBN-opzoeking" : "vaste schaal")})
            Datum: {a.Date:dd-MM-yyyy}
            Tijd: {a.TimeSlot}

            Een kalenderbestand (.ics) is bijgevoegd — je e-mailprogramma kan dit
            meestal automatisch toevoegen aan je agenda.
            """;

        await SendAsync(
            to: _app.OwnerEmail,
            subject: $"Afspraak {a.Date:dd-MM} {a.TimeSlot} - {a.Name}",
            body: body,
            icsContent: icsContent,
            icsFileName: icsFileName);
    }

    public async Task SendCustomerConfirmationAsync(Appointment a, string manageUrl)
    {
        var body = $"""
            Beste {a.Name},

            Uw afspraak is ontvangen voor {a.Date:dddd d MMMM yyyy} om {a.TimeSlot}.
            Geschatte prijsindicatie: € {a.EstimatedPriceEuro:0.00} (definitieve prijs wordt ter plekke afgesproken).

            Wilt u wijzigen of annuleren? Gebruik uw persoonlijke link:
            {manageUrl}

            Tot dan!
            """;

        await SendAsync(to: a.Email, subject: "Bevestiging afspraak — boeken ophalen", body: body);
    }

    public async Task SendCustomerUpdateAsync(Appointment a, string manageUrl, string kind)
    {
        // kind: "cancelled" or "rescheduled"
        var body = kind == "cancelled"
            ? $"""
               Beste {a.Name},

               Uw afspraak voor {a.Date:dddd d MMMM yyyy} om {a.TimeSlot} is geannuleerd.
               Wilt u alsnog een moment inplannen? Ga naar {_app.PublicBaseUrl}
               """
            : $"""
               Beste {a.Name},

               Uw afspraak is verzet naar {a.Date:dddd d MMMM yyyy} om {a.TimeSlot}.

               Wijzigen of annuleren? Gebruik uw persoonlijke link:
               {manageUrl}
               """;

        await SendAsync(
            to: a.Email,
            subject: kind == "cancelled" ? "Afspraak geannuleerd" : "Afspraak gewijzigd",
            body: body);
    }

    private async Task SendAsync(string to, string subject, string body, string? icsContent = null, string? icsFileName = null)
    {
        if (string.IsNullOrWhiteSpace(_smtp.Host))
        {
            _logger.LogWarning("SMTP not configured — skipping email to {To} ({Subject})", to, subject);
            return;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_smtp.FromName, _smtp.User));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            var builder = new BodyBuilder { TextBody = body };
            if (icsContent is not null)
            {
                var icsContentType = new ContentType("text", "calendar");
                icsContentType.Parameters.Add("method", "PUBLISH");
                icsContentType.Parameters.Add("charset", "UTF-8");
                builder.Attachments.Add(icsFileName ?? "afspraak.ics",
                    System.Text.Encoding.UTF8.GetBytes(icsContent),
                    icsContentType);
            }
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_smtp.Host, _smtp.Port, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_smtp.User, _smtp.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            // A failed email should never break the booking flow itself —
            // the appointment is already safely stored in the database.
            _logger.LogError(ex, "Failed to send email to {To}", to);
        }
    }
}
