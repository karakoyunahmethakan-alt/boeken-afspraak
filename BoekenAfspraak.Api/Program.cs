using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using BoekenAfspraak.Api.Data;
using BoekenAfspraak.Api.Models;
using BoekenAfspraak.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuration ----------
var dataDir = Environment.GetEnvironmentVariable("DATA_DIR")
              ?? builder.Configuration["DataDir"]
              ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDir);
Directory.CreateDirectory(Path.Combine(dataDir, "uploads"));

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
builder.Services.Configure<BrevoOptions>(builder.Configuration.GetSection("Brevo"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

// Railway's Raw Editor can leave stray quotes/whitespace around a pasted
// value, which silently breaks API auth without any obvious error. Strip
// them right after binding so every consumer of these options gets clean
// values.
static string CleanConfigValue(string value) => value.Trim('"', ' ');
builder.Services.PostConfigure<BrevoOptions>(o =>
{
    o.ApiKey = CleanConfigValue(o.ApiKey);
    o.SenderEmail = CleanConfigValue(o.SenderEmail);
    o.SenderName = CleanConfigValue(o.SenderName);
});
builder.Services.PostConfigure<AppOptions>(o =>
{
    o.OwnerEmail = CleanConfigValue(o.OwnerEmail);
    o.PublicBaseUrl = CleanConfigValue(o.PublicBaseUrl);
    o.TimeZoneId = CleanConfigValue(o.TimeZoneId);
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDir, "afspraken.db")}"));

builder.Services.AddSingleton<PricingService>();
builder.Services.AddHttpClient<EmailService>();
builder.Services.AddSingleton<JwtTokenService>();

var jwtSigningKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(jwtSigningKey))
    throw new InvalidOperationException("Jwt:SigningKey (env Jwt__SigningKey) is verplicht — genereer een lange willekeurige string.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "boeken-afspraak";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtIssuer,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey))
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

var tz = TimeZoneInfo.FindSystemTimeZoneById(
    app.Configuration["App:TimeZoneId"] ?? "Europe/Amsterdam");

// ---------- DB init + admin seed ----------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    if (!db.AdminUsers.Any())
    {
        var seedEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL");
        var seedPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
        if (!string.IsNullOrWhiteSpace(seedEmail) && !string.IsNullOrWhiteSpace(seedPassword))
        {
            db.AdminUsers.Add(new AdminUser
            {
                Email = seedEmail.Trim().ToLowerInvariant(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(seedPassword)
            });
            db.SaveChanges();
            app.Logger.LogInformation("Admin user seeded for {Email}", seedEmail);
        }
        else
        {
            app.Logger.LogWarning(
                "Geen admin account aanwezig en ADMIN_EMAIL/ADMIN_PASSWORD niet gezet — /admin kan niet inloggen tot dit is opgelost.");
        }
    }
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

var appOpts = app.Services.GetRequiredService<IOptions<AppOptions>>().Value;

// ================= Public config & availability =================

app.MapGet("/api/config", () => Results.Ok(new
{
    slotTimes = appOpts.SlotTimes,
    minBooks = appOpts.MinBooks,
    daysAhead = appOpts.DaysAhead,
    ownerEmail = appOpts.OwnerEmail
}));

app.MapGet("/api/availability", async (string date, AppDbContext db) =>
{
    if (!DateOnly.TryParse(date, out var d))
        return Results.BadRequest(new { error = "Ongeldige datum." });

    var taken = await db.Appointments
        .Where(a => a.Date == d && a.Status == AppointmentStatus.Confirmed)
        .Select(a => a.TimeSlot)
        .ToListAsync();

    var slots = appOpts.SlotTimes.Select(t => new { time = t, available = !taken.Contains(t) });
    return Results.Ok(new { date, slots });
});

// ================= Booking =================

app.MapPost("/api/appointments", async (
    CreateAppointmentRequest req,
    AppDbContext db,
    PricingService pricing,
    EmailService email) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Address) || string.IsNullOrWhiteSpace(req.Email))
        return Results.BadRequest(new { error = "Naam, adres en e-mail zijn verplicht." });
    if (!System.Text.RegularExpressions.Regex.IsMatch(req.Email, @"^\S+@\S+\.\S+$"))
        return Results.BadRequest(new { error = "Vul een geldig e-mailadres in." });
    if (req.BookCount < appOpts.MinBooks)
        return Results.BadRequest(new { error = $"Minimaal {appOpts.MinBooks} boeken vereist." });
    if (!appOpts.SlotTimes.Contains(req.TimeSlot))
        return Results.BadRequest(new { error = "Ongeldig tijdstip." });
    if (!DateOnly.TryParse(req.Date, out var date))
        return Results.BadRequest(new { error = "Ongeldige datum." });
    var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz).Date);
    if (date < today || date > today.AddDays(appOpts.DaysAhead))
        return Results.BadRequest(new { error = "Datum ligt buiten het boekbare venster." });

    var appointment = new Appointment
    {
        Name = req.Name.Trim(),
        Address = req.Address.Trim(),
        Email = req.Email.Trim(),
        Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim(),
        BookCount = req.BookCount,
        BookType = string.IsNullOrWhiteSpace(req.BookType) ? null : req.BookType.Trim(),
        Date = date,
        TimeSlot = req.TimeSlot,
        Status = AppointmentStatus.Confirmed
    };

    appointment.EstimatedPriceEuro = pricing.CalculateFlatEstimate(req.BookCount);

    db.Appointments.Add(appointment);
    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException)
    {
        return Results.Conflict(new { error = "Dit tijdstip is net bezet geraakt. Kies een ander moment." });
    }

    var manageUrl = $"{appOpts.PublicBaseUrl.TrimEnd('/')}/beheer.html?token={appointment.ManageToken}";
    var ics = IcsBuilder.Build(appointment.ManageToken, $"Boeken ophalen — {appointment.Name}",
        $"{appointment.BookCount} boeken, {appointment.Address}. Contact: {appointment.Phone ?? appointment.Email}",
        appointment.Date, appointment.TimeSlot, tz, durationMinutes: 12, status: "CONFIRMED", sequence: 0);

    // Fire-and-forget: SMTP on Railway can be slow/blocked, and the customer
    // shouldn't have to wait for two outgoing emails before the booking
    // confirms. EmailService already swallows its own send failures (logged
    // there); this try/catch is just defense in depth for anything else
    // that might escape it.
    var appointmentIdForLog = appointment.Id;
    _ = Task.Run(async () =>
    {
        try
        {
            await email.SendOwnerNotificationAsync(appointment, ics, "afspraak.ics");
            await email.SendCustomerConfirmationAsync(appointment, manageUrl);
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Achtergrond e-mailverzending voor afspraak {Id} is mislukt.", appointmentIdForLog);
        }
    });

    return Results.Created($"/api/appointments/manage/{appointment.ManageToken}",
        new AppointmentResultDto(appointment.ManageToken, appointment.Date.ToString("yyyy-MM-dd"), appointment.TimeSlot,
            appointment.EstimatedPriceEuro));
});

// Optional photo upload, done as a follow-up call against the manage token.
app.MapPost("/api/appointments/manage/{token:guid}/photos", async (Guid token, HttpRequest http, AppDbContext db) =>
{
    var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.ManageToken == token);
    if (appointment is null) return Results.NotFound();
    if (appointment.Status != AppointmentStatus.Confirmed) return Results.BadRequest(new { error = "Deze afspraak is niet meer actief." });

    if (!http.HasFormContentType) return Results.BadRequest(new { error = "Verwacht multipart/form-data." });
    var form = await http.ReadFormAsync();
    if (form.Files.Count == 0) return Results.BadRequest(new { error = "Geen bestanden ontvangen." });
    if (form.Files.Count > 5) return Results.BadRequest(new { error = "Maximaal 5 foto's." });

    var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
    var folder = Path.Combine(dataDir, "uploads", token.ToString());
    Directory.CreateDirectory(folder);

    var savedNames = new List<string>();
    foreach (var file in form.Files)
    {
        if (!allowed.Contains(file.ContentType) || file.Length > 8 * 1024 * 1024) continue;
        var safeName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        await using var fs = File.Create(Path.Combine(folder, safeName));
        await file.CopyToAsync(fs);
        savedNames.Add(safeName);
    }

    var existing = (appointment.PhotoFileNames ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    existing.AddRange(savedNames);
    appointment.PhotoFileNames = string.Join(',', existing);
    await db.SaveChangesAsync();

    return Results.Ok(new { uploaded = savedNames.Count });
});

// ================= Customer self-service (token based) =================

app.MapGet("/api/appointments/manage/{token:guid}", async (Guid token, AppDbContext db) =>
{
    var a = await db.Appointments.FirstOrDefaultAsync(x => x.ManageToken == token);
    if (a is null) return Results.NotFound();
    return Results.Ok(new
    {
        a.Name, a.Address, a.BookCount, a.BookType,
        date = a.Date.ToString("yyyy-MM-dd"),
        a.TimeSlot,
        status = a.Status.ToString(),
        a.EstimatedPriceEuro
    });
});

app.MapPost("/api/appointments/manage/{token:guid}/cancel", async (Guid token, AppDbContext db, EmailService email) =>
{
    var a = await db.Appointments.FirstOrDefaultAsync(x => x.ManageToken == token);
    if (a is null) return Results.NotFound();
    if (a.Status == AppointmentStatus.Cancelled) return Results.Ok(new { alreadyCancelled = true });

    a.Status = AppointmentStatus.Cancelled;
    a.CancelledAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    var ics = IcsBuilder.Build(a.ManageToken, $"Boeken ophalen — {a.Name}", "Geannuleerd", a.Date, a.TimeSlot, tz, 12, "CANCELLED", sequence: 1);
    var appointmentIdForLog = a.Id;
    _ = Task.Run(async () =>
    {
        try
        {
            await email.SendOwnerNotificationAsync(a, ics, "afspraak-geannuleerd.ics");
            await email.SendCustomerUpdateAsync(a, "", "cancelled");
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Achtergrond e-mailverzending voor annulering van afspraak {Id} is mislukt.", appointmentIdForLog);
        }
    });

    return Results.Ok(new { cancelled = true });
});

app.MapPost("/api/appointments/manage/{token:guid}/reschedule", async (Guid token, RescheduleRequest req, AppDbContext db, EmailService email) =>
{
    var a = await db.Appointments.FirstOrDefaultAsync(x => x.ManageToken == token);
    if (a is null) return Results.NotFound();
    if (a.Status != AppointmentStatus.Confirmed) return Results.BadRequest(new { error = "Deze afspraak is niet meer actief." });
    if (!appOpts.SlotTimes.Contains(req.NewTimeSlot)) return Results.BadRequest(new { error = "Ongeldig tijdstip." });
    if (!DateOnly.TryParse(req.NewDate, out var newDate)) return Results.BadRequest(new { error = "Ongeldige datum." });

    if (a.OriginalDate is null)
    {
        a.OriginalDate = a.Date;
        a.OriginalTimeSlot = a.TimeSlot;
    }
    a.Date = newDate;
    a.TimeSlot = req.NewTimeSlot;
    a.RescheduleCount += 1;

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException)
    {
        return Results.Conflict(new { error = "Dit tijdstip is net bezet geraakt. Kies een ander moment." });
    }

    var manageUrl = $"{appOpts.PublicBaseUrl.TrimEnd('/')}/beheer.html?token={a.ManageToken}";
    var ics = IcsBuilder.Build(a.ManageToken, $"Boeken ophalen — {a.Name}", "Verzet naar nieuw tijdstip", a.Date, a.TimeSlot, tz, 12, "CONFIRMED", sequence: a.RescheduleCount);
    var appointmentIdForLog = a.Id;
    _ = Task.Run(async () =>
    {
        try
        {
            await email.SendOwnerNotificationAsync(a, ics, "afspraak-gewijzigd.ics");
            await email.SendCustomerUpdateAsync(a, manageUrl, "rescheduled");
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Achtergrond e-mailverzending voor verzetten van afspraak {Id} is mislukt.", appointmentIdForLog);
        }
    });

    return Results.Ok(new { rescheduled = true, date = a.Date.ToString("yyyy-MM-dd"), a.TimeSlot });
});

// ================= Admin =================

app.MapPost("/api/admin/login", async (AdminLoginRequest req, AppDbContext db, JwtTokenService jwt) =>
{
    var email = req.Email.Trim().ToLowerInvariant();
    var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Email == email);
    if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        return Results.Unauthorized();

    var (token, expires) = jwt.IssueToken(user.Email);
    return Results.Ok(new AdminLoginResponse(token, expires));
});

var admin = app.MapGroup("/api/admin").RequireAuthorization();

admin.MapGet("/appointments", async (AppDbContext db, string? status) =>
{
    var query = db.Appointments.AsQueryable();
    if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AppointmentStatus>(status, true, out var st))
        query = query.Where(a => a.Status == st);

    var list = await query.OrderBy(a => a.Date).ThenBy(a => a.TimeSlot).ToListAsync();
    return Results.Ok(list.Select(a => new AdminAppointmentDto(
        a.Id, a.Name, a.Address, a.Email, a.Phone, a.BookCount, a.BookType,
        a.Date.ToString("yyyy-MM-dd"), a.TimeSlot, a.Status.ToString(),
        a.EstimatedPriceEuro,
        a.OriginalDate?.ToString("yyyy-MM-dd"), a.OriginalTimeSlot, a.RescheduleCount, a.CreatedAtUtc)));
});

admin.MapPost("/appointments/{id:int}/cancel", async (int id, AppDbContext db, EmailService email) =>
{
    var a = await db.Appointments.FindAsync(id);
    if (a is null) return Results.NotFound();
    if (a.Status == AppointmentStatus.Cancelled) return Results.Ok(new { alreadyCancelled = true });

    a.Status = AppointmentStatus.Cancelled;
    a.CancelledAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    var ics = IcsBuilder.Build(a.ManageToken, $"Boeken ophalen — {a.Name}", "Geannuleerd door beheerder", a.Date, a.TimeSlot, tz, 12, "CANCELLED", sequence: 1);
    var appointmentIdForLog = a.Id;
    _ = Task.Run(async () =>
    {
        try
        {
            await email.SendOwnerNotificationAsync(a, ics, "afspraak-geannuleerd.ics");
            await email.SendCustomerUpdateAsync(a, "", "cancelled");
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Achtergrond e-mailverzending voor annulering (admin) van afspraak {Id} is mislukt.", appointmentIdForLog);
        }
    });

    return Results.Ok(new { cancelled = true });
});

admin.MapGet("/appointments/export.csv", async (AppDbContext db) =>
{
    var all = await db.Appointments.OrderBy(a => a.Date).ToListAsync();
    var bytes = CsvExportService.BuildAppointmentsCsv(all);
    return Results.File(bytes, "text/csv", $"afspraken-{DateTime.UtcNow:yyyyMMdd}.csv");
});

// TEMP DEBUG: confirm the Brevo environment variables actually made it into
// the app's configuration (ApiKey itself is deliberately never logged).
var brevoOpts = app.Services.GetRequiredService<IOptions<BrevoOptions>>().Value;
app.Logger.LogInformation(
    "Brevo config check — ApiKey: {ApiKeyStatus}, SenderEmail: {SenderEmailStatus}",
    string.IsNullOrWhiteSpace(brevoOpts.ApiKey) ? "LEEG" : "OK",
    string.IsNullOrWhiteSpace(brevoOpts.SenderEmail) ? "LEEG" : brevoOpts.SenderEmail);

app.Run();

// Exposes the top-level Program for WebApplicationFactory<Program> in integration tests.
public partial class Program { }
