namespace BoekenAfspraak.Api.Services;

public class AppOptions
{
    public string[] SlotTimes { get; set; } =
        { "18:00","18:12","18:24","18:36","18:48","19:00","19:12","19:24","19:36","19:48" };

    public int MinBooks { get; set; } = 10;
    public int DaysAhead { get; set; } = 14;
    public string TimeZoneId { get; set; } = "Europe/Amsterdam";

    // Flat pricing: euros per full block of BlockSize books, remainder ignored.
    public decimal FlatPricePerBlock { get; set; } = 3.50m;
    public int FlatBlockSize { get; set; } = 10;

    // ISBN-based pricing tiers (per book, based on the Bol.com 2nd-hand price found).
    public decimal IsbnLowTierMaxSourcePrice { get; set; } = 20.00m; // 2nd-hand price boundary
    public decimal IsbnLowTierOffer { get; set; } = 1.00m;           // offer when source price <= boundary
    public decimal IsbnHighTierOffer { get; set; } = 1.50m;          // offer when source price > boundary
    // Offer used when a book's ISBN can't be found/priced on Bol.com at all.
    public decimal IsbnFallbackOffer { get; set; } = 1.00m;

    public string OwnerEmail { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "http://localhost:5000";
}

public class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromName { get; set; } = "Boeken ophalen";
}

public class JwtOptions
{
    public string SigningKey { get; set; } = "";
    public string Issuer { get; set; } = "boeken-afspraak";
    public int ExpiryMinutes { get; set; } = 120;
}
