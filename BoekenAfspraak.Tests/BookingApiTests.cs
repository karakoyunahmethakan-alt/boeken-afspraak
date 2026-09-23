using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BoekenAfspraak.Api.Models;
using BoekenAfspraak.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BoekenAfspraak.Tests;

// End-to-end integration tests against the real app (Program.cs), running on
// an in-process TestServer with a disposable temp SQLite database and a
// faked-out Brevo email endpoint (see CustomWebApplicationFactory). All
// scenarios share one factory/app instance for the whole class, so every
// test that books an appointment uses its own dedicated day offset to avoid
// colliding with another test's slots.
public class BookingApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly string[] AllSlotTimes =
        { "18:00", "18:12", "18:24", "18:36", "18:48", "19:00", "19:12", "19:24", "19:36", "19:48" };

    public BookingApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // Distinct, non-overlapping booking days per scenario so tests sharing
    // one app/db instance never collide on the (date, timeslot) unique
    // constraint. Offsets 1..13 always stay inside the DaysAhead=14 window.
    private static string DateFor(int dayOffset) =>
        DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(dayOffset)).ToString("yyyy-MM-dd");

    private static CreateAppointmentRequest MakeRequest(
        string date, string timeSlot, int bookCount = 12, string email = "klant@example.com", string name = "Test Klant") =>
        new(name, "Teststraat 1, Voorbeeldstad", email, null, bookCount, null, date, timeSlot);

    private async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/admin/login",
            new AdminLoginRequest(CustomWebApplicationFactory.AdminEmail, CustomWebApplicationFactory.AdminPassword));
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<AdminLoginResponse>(JsonOpts);
        return body!.Token;
    }

    // a) GET /api/config
    [Fact]
    public async Task GetConfig_ReturnsExpectedDefaults()
    {
        var res = await _client.GetAsync("/api/config");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(10, json.GetProperty("minBooks").GetInt32());
        Assert.Equal(14, json.GetProperty("daysAhead").GetInt32());
        Assert.Equal(10, json.GetProperty("slotTimes").GetArrayLength());
    }

    // b) POST /api/appointments — flat pricing + min-books validation
    [Fact]
    public async Task CreateAppointment_FlatPricing_20Books_Returns700()
    {
        var res = await _client.PostAsJsonAsync("/api/appointments", MakeRequest(DateFor(1), "18:00", bookCount: 20, email: "prijs20@example.com"));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<AppointmentResultDto>(JsonOpts);
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.ManageToken);
        Assert.Equal(7.00m, body.EstimatedPriceEuro);
    }

    [Fact]
    public async Task CreateAppointment_FlatPricing_15Books_Returns350()
    {
        var res = await _client.PostAsJsonAsync("/api/appointments", MakeRequest(DateFor(1), "18:12", bookCount: 15, email: "prijs15@example.com"));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<AppointmentResultDto>(JsonOpts);
        Assert.Equal(3.50m, body!.EstimatedPriceEuro);
    }

    [Fact]
    public async Task CreateAppointment_BelowMinBooks_IsRejected()
    {
        var res = await _client.PostAsJsonAsync("/api/appointments", MakeRequest(DateFor(1), "18:24", bookCount: 9, email: "teweinig@example.com"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // c) double booking same slot -> 409
    [Fact]
    public async Task CreateAppointment_SameSlotTwice_SecondIsConflict()
    {
        var date = DateFor(2);
        var first = await _client.PostAsJsonAsync("/api/appointments", MakeRequest(date, "18:00", email: "eerste@example.com"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.PostAsJsonAsync("/api/appointments", MakeRequest(date, "18:00", email: "tweede@example.com"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // d) fill a whole day, then check availability + an 11th attempt
    [Fact]
    public async Task FillingAllSlotsOnADay_MarksThemUnavailable_AndFurtherBookingConflicts()
    {
        var date = DateFor(3);
        foreach (var slot in AllSlotTimes)
        {
            var res = await _client.PostAsJsonAsync("/api/appointments",
                MakeRequest(date, slot, email: $"slot{slot.Replace(":", "")}@example.com"));
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        }

        var availRes = await _client.GetAsync($"/api/availability?date={date}");
        availRes.EnsureSuccessStatusCode();
        var availJson = await availRes.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var slot in availJson.GetProperty("slots").EnumerateArray())
            Assert.False(slot.GetProperty("available").GetBoolean());

        var extra = await _client.PostAsJsonAsync("/api/appointments",
            MakeRequest(date, AllSlotTimes[0], email: "elfde@example.com"));
        Assert.Equal(HttpStatusCode.Conflict, extra.StatusCode);
    }

    // e) invalid email format -> 400
    [Fact]
    public async Task CreateAppointment_InvalidEmailFormat_IsRejected()
    {
        var res = await _client.PostAsJsonAsync("/api/appointments", MakeRequest(DateFor(4), "18:00", email: "bozuk-mail"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // f) manage-token GET: valid vs invalid
    [Fact]
    public async Task GetByManageToken_ValidToken_ReturnsAppointment_InvalidToken_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/api/appointments", MakeRequest(DateFor(5), "18:00", name: "Token Test", email: "token@example.com"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<AppointmentResultDto>(JsonOpts);

        var getRes = await _client.GetAsync($"/api/appointments/manage/{created!.ManageToken}");
        getRes.EnsureSuccessStatusCode();
        var body = await getRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Token Test", body.GetProperty("name").GetString());

        var notFound = await _client.GetAsync($"/api/appointments/manage/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    // g) cancel frees the slot for a new booking
    [Fact]
    public async Task CancelAppointment_SetsCancelled_AndFreesSlotForNewBooking()
    {
        var date = DateFor(6);
        var create = await _client.PostAsJsonAsync("/api/appointments", MakeRequest(date, "18:00", email: "annuleren@example.com"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<AppointmentResultDto>(JsonOpts);

        var cancelRes = await _client.PostAsync($"/api/appointments/manage/{created!.ManageToken}/cancel", content: null);
        cancelRes.EnsureSuccessStatusCode();

        var getRes = await _client.GetAsync($"/api/appointments/manage/{created.ManageToken}");
        var body = await getRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cancelled", body.GetProperty("status").GetString());

        var rebook = await _client.PostAsJsonAsync("/api/appointments", MakeRequest(date, "18:00", email: "nieuweklant@example.com"));
        Assert.Equal(HttpStatusCode.Created, rebook.StatusCode);
    }

    // h) reschedule frees the old slot, occupies the new one, bumps RescheduleCount
    [Fact]
    public async Task RescheduleAppointment_MovesSlotAndFreesOldOne()
    {
        var oldDate = DateFor(7);
        var newDate = DateFor(8);
        const string email = "verzetten@example.com";

        var create = await _client.PostAsJsonAsync("/api/appointments", MakeRequest(oldDate, "18:00", email: email));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<AppointmentResultDto>(JsonOpts);

        var rescheduleRes = await _client.PostAsJsonAsync(
            $"/api/appointments/manage/{created!.ManageToken}/reschedule",
            new RescheduleRequest(newDate, "18:12"));
        rescheduleRes.EnsureSuccessStatusCode();

        // Old slot must be free again.
        var rebookOld = await _client.PostAsJsonAsync("/api/appointments", MakeRequest(oldDate, "18:00", email: "anderklant@example.com"));
        Assert.Equal(HttpStatusCode.Created, rebookOld.StatusCode);

        // New slot must now be taken.
        var conflictNew = await _client.PostAsJsonAsync("/api/appointments", MakeRequest(newDate, "18:12", email: "nogeenklant@example.com"));
        Assert.Equal(HttpStatusCode.Conflict, conflictNew.StatusCode);

        using var adminClient = _factory.CreateClient();
        var token = await GetAdminTokenAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var list = await adminClient.GetFromJsonAsync<List<AdminAppointmentDto>>("/api/admin/appointments", JsonOpts);
        var updated = list!.Single(a => a.Email == email);

        Assert.Equal(1, updated.RescheduleCount);
        Assert.Equal(newDate, updated.Date);
        Assert.Equal("18:12", updated.TimeSlot);
        Assert.Equal(oldDate, updated.OriginalDate);
        Assert.Equal("18:00", updated.OriginalTimeSlot);
    }

    // i) admin login: correct vs wrong password
    [Fact]
    public async Task AdminLogin_CorrectCredentials_ReturnsToken_WrongPassword_Returns401()
    {
        var okRes = await _client.PostAsJsonAsync("/api/admin/login",
            new AdminLoginRequest(CustomWebApplicationFactory.AdminEmail, CustomWebApplicationFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);
        var body = await okRes.Content.ReadFromJsonAsync<AdminLoginResponse>(JsonOpts);
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));

        var badRes = await _client.PostAsJsonAsync("/api/admin/login",
            new AdminLoginRequest(CustomWebApplicationFactory.AdminEmail, "helemaal-fout-wachtwoord"));
        Assert.Equal(HttpStatusCode.Unauthorized, badRes.StatusCode);
    }

    // j) admin list requires auth
    [Fact]
    public async Task AdminAppointmentsList_WithoutToken_Returns401_WithToken_Returns200()
    {
        var noAuthRes = await _client.GetAsync("/api/admin/appointments");
        Assert.Equal(HttpStatusCode.Unauthorized, noAuthRes.StatusCode);

        using var adminClient = _factory.CreateClient();
        var token = await GetAdminTokenAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var okRes = await adminClient.GetAsync("/api/admin/appointments");
        Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);
        var list = await okRes.Content.ReadFromJsonAsync<List<AdminAppointmentDto>>(JsonOpts);
        Assert.NotNull(list);
    }

    // k) CSV export
    [Fact]
    public async Task AdminCsvExport_ReturnsNonEmptyCsv()
    {
        await _client.PostAsJsonAsync("/api/appointments", MakeRequest(DateFor(9), "18:00", email: "csvtest@example.com"));

        using var adminClient = _factory.CreateClient();
        var token = await GetAdminTokenAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await adminClient.GetAsync("/api/admin/appointments/export.csv");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/csv", res.Content.Headers.ContentType?.MediaType);
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);
    }

    // l) admin cancel + no-op re-cancel
    [Fact]
    public async Task AdminCancel_CancelsAppointment_ReCancelIsNoOp()
    {
        var create = await _client.PostAsJsonAsync("/api/appointments", MakeRequest(DateFor(10), "18:00", email: "admincancel@example.com"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var adminClient = _factory.CreateClient();
        var token = await GetAdminTokenAsync(adminClient);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var list = await adminClient.GetFromJsonAsync<List<AdminAppointmentDto>>("/api/admin/appointments", JsonOpts);
        var id = list!.Single(a => a.Email == "admincancel@example.com").Id;

        var cancelRes = await adminClient.PostAsync($"/api/admin/appointments/{id}/cancel", content: null);
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

        // Re-cancel of an already-cancelled appointment must not crash — it
        // should be a clean no-op response.
        var reCancelRes = await adminClient.PostAsync($"/api/admin/appointments/{id}/cancel", content: null);
        Assert.Equal(HttpStatusCode.OK, reCancelRes.StatusCode);
        var reCancelBody = await reCancelRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(reCancelBody.GetProperty("alreadyCancelled").GetBoolean());
    }

    // Extra: EmailService itself, wired to the fake Brevo handler, must not
    // throw for either the owner-notification or customer-confirmation path.
    // This is the "did the attempt crash" check requested in place of
    // actually sending real email.
    [Fact]
    public async Task EmailService_SendViaFakeBrevoHandler_DoesNotThrow()
    {
        using var scope = _factory.Services.CreateScope();
        var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();

        var appointment = new Appointment
        {
            Name = "E-mail Test",
            Address = "Teststraat 1",
            Email = "emailtest@example.com",
            BookCount = 12,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(11)),
            TimeSlot = "18:00",
            EstimatedPriceEuro = 3.50m
        };

        var ownerEx = await Record.ExceptionAsync(() =>
            emailService.SendOwnerNotificationAsync(appointment, "BEGIN:VCALENDAR\nEND:VCALENDAR", "test.ics"));
        var customerEx = await Record.ExceptionAsync(() =>
            emailService.SendCustomerConfirmationAsync(appointment, "http://localhost/beheer.html?token=abc"));

        Assert.Null(ownerEx);
        Assert.Null(customerEx);
    }
}
