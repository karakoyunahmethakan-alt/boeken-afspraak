namespace BoekenAfspraak.Tests;

// Same app/setup as CustomWebApplicationFactory, but with the real,
// production-sized booking rate limit (see appsettings.json), so
// RateLimitTests can exercise the actual limiting behavior without
// affecting the shared, high-limit factory used by every other test class.
public class LowRateLimitWebApplicationFactory : CustomWebApplicationFactory
{
    protected override int BookingRateLimitPermitLimit => 5;
}
