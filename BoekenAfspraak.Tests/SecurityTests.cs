using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BoekenAfspraak.Api.Data;
using BoekenAfspraak.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoekenAfspraak.Tests;

// Honeypot spam guard and admin-only photo access, against the shared
// high-rate-limit CustomWebApplicationFactory (see its
// BookingRateLimitPermitLimit) so these scenarios aren't affected by the
// booking rate limiter — that's covered separately in RateLimitTests.
public class SecurityTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public SecurityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string DateFor(int dayOffset) =>
        DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(dayOffset)).ToString("yyyy-MM-dd");

    private async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/admin/login",
            new AdminLoginRequest(CustomWebApplicationFactory.AdminEmail, CustomWebApplicationFactory.AdminPassword));
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<AdminLoginResponse>(JsonOpts);
        return body!.Token;
    }

    [Fact]
    public async Task CreateAppointment_WithHoneypotFilled_ReturnsOk_ButDoesNotPersistAnything()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.Appointments.CountAsync();

        var body = new
        {
            name = "Bot",
            address = "Botstraat 1",
            email = "bot@example.com",
            phone = (string?)null,
            bookCount = 12,
            bookType = (string?)null,
            date = DateFor(12),
            timeSlot = "18:00",
            honeypot = "http://spam.example"
        };
        var res = await _client.PostAsJsonAsync("/api/appointments", body);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var after = await db.Appointments.CountAsync();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task PhotoEndpoint_WithoutAdminToken_IsRejected_WithValidToken_ReturnsThePhoto()
    {
        var date = DateFor(13);
        var create = await _client.PostAsJsonAsync("/api/appointments",
            new CreateAppointmentRequest("Foto Test", "Fotostraat 1", "fototest@example.com", null, 12, null, date, "18:12"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<AppointmentResultDto>(JsonOpts);

        var fileBytes = new byte[] { 1, 2, 3, 4 };
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "files", "test.jpg");
        var uploadRes = await _client.PostAsync($"/api/appointments/manage/{created!.ManageToken}/photos", content);
        uploadRes.EnsureSuccessStatusCode();

        using var adminClient = _factory.CreateClient();
        var token = await GetAdminTokenAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var list = await adminClient.GetFromJsonAsync<List<AdminAppointmentDto>>("/api/admin/appointments", JsonOpts);
        var appt = list!.Single(a => a.Email == "fototest@example.com");
        var fileName = Assert.Single(appt.PhotoFileNames);

        var noAuthRes = await _client.GetAsync($"/api/admin/appointments/{appt.Id}/photos/{fileName}");
        Assert.Equal(HttpStatusCode.Unauthorized, noAuthRes.StatusCode);

        var authedRes = await adminClient.GetAsync($"/api/admin/appointments/{appt.Id}/photos/{fileName}");
        Assert.Equal(HttpStatusCode.OK, authedRes.StatusCode);
        Assert.Equal(fileBytes, await authedRes.Content.ReadAsByteArrayAsync());
    }
}
