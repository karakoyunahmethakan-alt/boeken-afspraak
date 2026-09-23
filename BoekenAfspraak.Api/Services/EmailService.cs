using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using BoekenAfspraak.Api.Models;

namespace BoekenAfspraak.Api.Services;

// Sends transactional email via Brevo's HTTP API (https://api.brevo.com/v3/smtp/email)
// instead of MailKit/SMTP. Railway blocks outgoing SMTP (port 587), which made
// SmtpClient.ConnectAsync hang and time out; Brevo's API runs over plain HTTPS
// (port 443), which isn't affected by that block.
public class EmailService
{
    private static readonly Uri BrevoSendEndpoint = new("https://api.brevo.com/v3/smtp/email");

    private readonly HttpClient _http;
    private readonly BrevoOptions _brevo;
    private readonly AppOptions _app;
    private readonly ILogger<EmailService> _logger;

    public EmailService(HttpClient http, IOptions<BrevoOptions> brevo, IOptions<AppOptions> app, ILogger<EmailService> logger)
    {
        _http = http;
        _brevo = brevo.Value;
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
            Geschatte prijsindicatie: € {a.EstimatedPriceEuro:0.00} (vaste schaal)
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
        if (string.IsNullOrWhiteSpace(_brevo.ApiKey))
        {
            _logger.LogWarning("Brevo not configured — skipping email to {To} ({Subject})", to, subject);
            return;
        }

        try
        {
            List<BrevoAttachment>? attachments = null;
            if (icsContent is not null)
            {
                attachments = new List<BrevoAttachment>
                {
                    new BrevoAttachment(
                        Content: Convert.ToBase64String(Encoding.UTF8.GetBytes(icsContent)),
                        Name: icsFileName ?? "afspraak.ics")
                };
            }

            var payload = new BrevoSendRequest(
                Sender: new BrevoSender(_brevo.SenderName, _brevo.SenderEmail),
                To: new List<BrevoRecipient> { new(to) },
                Subject: subject,
                TextContent: body,
                Attachment: attachments);

            using var request = new HttpRequestMessage(HttpMethod.Post, BrevoSendEndpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("api-key", _brevo.ApiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json");

            // Bound the whole HTTP round-trip so a stuck outgoing connection
            // can't hang this background task forever — it just times out,
            // logs, and gives up.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await _http.SendAsync(request, cts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var brevoMessage = TryExtractBrevoErrorMessage(responseBody);
                _logger.LogError(
                    "Brevo e-mail naar {To} ({Subject}) mislukt met status {Status}: {Message}. Raw response: {Body}",
                    to, subject, (int)response.StatusCode, brevoMessage ?? "(geen message-veld)", responseBody);
                Console.WriteLine(
                    $"[EmailService] Brevo send to {to} ({subject}) failed with status {(int)response.StatusCode}: " +
                    $"{brevoMessage ?? "(no message field)"}. Raw response: {responseBody}");
            }
        }
        catch (Exception ex)
        {
            // A failed email should never break the booking flow itself —
            // the appointment is already safely stored in the database.
            _logger.LogError(ex, "Failed to send email to {To}", to);

            // TEMP DEBUG: also write the full exception (with stack trace) to
            // the console so it's easy to spot in Railway's deploy logs.
            Console.WriteLine($"[EmailService] Failed to send email to {to} ({subject}): {ex}");
        }
    }

    private static string? TryExtractBrevoErrorMessage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement.TryGetProperty("message", out var msg) ? msg.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private record BrevoSendRequest(
        [property: JsonPropertyName("sender")] BrevoSender Sender,
        [property: JsonPropertyName("to")] List<BrevoRecipient> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("textContent")] string TextContent,
        [property: JsonPropertyName("attachment")] List<BrevoAttachment>? Attachment);

    private record BrevoSender(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("email")] string Email);

    private record BrevoRecipient([property: JsonPropertyName("email")] string Email);

    private record BrevoAttachment(
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("name")] string Name);
}
