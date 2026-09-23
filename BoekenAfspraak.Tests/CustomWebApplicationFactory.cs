using BoekenAfspraak.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BoekenAfspraak.Tests;

// Boots the real app (Program.cs) for integration tests, pointed entirely at
// disposable, test-only state:
//   - a fresh temp folder for DATA_DIR (SQLite db + uploads), never the
//     real App_Data/afspraken.db
//   - test-only Jwt/App/Brevo configuration values via UseSetting, never
//     the real appsettings.json / user-secrets
//   - a fake HTTP handler standing in for Brevo's API, so no real email is
//     ever sent
//
// ADMIN_EMAIL / ADMIN_PASSWORD and DATA_DIR are read by Program.cs directly
// via Environment.GetEnvironmentVariable (not IConfiguration), so they must
// be set as real process environment variables rather than via UseSetting.
// To keep that process-wide mutation safe, the whole test suite shares this
// one factory instance (IClassFixture) and xUnit test-collection
// parallelization is disabled (see AssemblyInfo.cs) so no other factory can
// interleave and stomp on these values.
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "boekenafspraak-tests-" + Guid.NewGuid().ToString("N"));

    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Test-Wachtwoord-123!";
    public const string OwnerEmail = "owner@test.local";

    // High by default so ordinary functional tests (which book many
    // appointments from what looks like a single client IP under the
    // in-memory TestServer) never trip the booking rate limiter.
    // RateLimitTests overrides this down to the real production value to
    // exercise the actual limiting behavior in isolation.
    protected virtual int BookingRateLimitPermitLimit => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(DataDir);
        Environment.SetEnvironmentVariable("DATA_DIR", DataDir);
        Environment.SetEnvironmentVariable("ADMIN_EMAIL", AdminEmail);
        Environment.SetEnvironmentVariable("ADMIN_PASSWORD", AdminPassword);

        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-lang-genoeg-voor-hmac-sha256-0123456789");
        builder.UseSetting("Jwt:Issuer", "boeken-afspraak-test");
        builder.UseSetting("App:OwnerEmail", OwnerEmail);
        builder.UseSetting("App:PublicBaseUrl", "http://localhost:5000");
        builder.UseSetting("Brevo:ApiKey", "test-key-niet-echt");
        builder.UseSetting("Brevo:SenderEmail", OwnerEmail);
        builder.UseSetting("Brevo:SenderName", "Boeken ophalen (test)");
        builder.UseSetting("RateLimit:BookingPermitLimit", BookingRateLimitPermitLimit.ToString());
        builder.UseSetting("RateLimit:BookingWindowMinutes", "10");

        builder.ConfigureServices(services =>
        {
            // Replace EmailService's real outbound HttpClient handler with a
            // fake one, so tests never call the real Brevo API.
            services.AddHttpClient<EmailService>()
                .ConfigurePrimaryHttpMessageHandler(() => new FakeBrevoHandler());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (Directory.Exists(DataDir))
                Directory.Delete(DataDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only — a locked SQLite file on Windows
            // right after disposal shouldn't fail the test run.
        }
    }
}
