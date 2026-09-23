using System.Net;
using System.Net.Http.Json;
using BoekenAfspraak.Api.Models;

namespace BoekenAfspraak.Tests;

// Uses its own factory (LowRateLimitWebApplicationFactory) with the real,
// production-sized limit (5 per 10 minutes) instead of the shared
// high-limit CustomWebApplicationFactory, so this doesn't affect — or get
// affected by — the booking scenarios in other test classes.
public class RateLimitTests : IClassFixture<LowRateLimitWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RateLimitTests(LowRateLimitWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static string DateFor(int dayOffset) =>
        DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(dayOffset)).ToString("yyyy-MM-dd");

    [Fact]
    public async Task SixthBookingAttemptFromSameClient_IsRateLimited()
    {
        // TestServer requests from one HttpClient share one rate-limit
        // partition (keyed by remote IP, which is constant here), so this
        // exercises "same IP" without needing to fake the address.
        var slots = new[] { "18:00", "18:12", "18:24", "18:36", "18:48", "19:00" };
        var date = DateFor(1);

        HttpResponseMessage? last = null;
        for (var i = 0; i < slots.Length; i++)
        {
            var req = new CreateAppointmentRequest(
                "Rate Test", "Teststraat 1", $"rate{i}@example.com", null, 12, null, date, slots[i]);
            last = await _client.PostAsJsonAsync("/api/appointments", req);
            if (i < slots.Length - 1)
                Assert.Equal(HttpStatusCode.Created, last.StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }
}
