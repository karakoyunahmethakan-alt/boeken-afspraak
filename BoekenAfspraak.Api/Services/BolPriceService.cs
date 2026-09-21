using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace BoekenAfspraak.Api.Services;

// Looks up the current second-hand ("tweedehands") price for a book on
// Bol.com by ISBN/EAN, so the booking form can show a max-offer estimate.
//
// IMPORTANT: the exact REST path for "competing offers by EAN" can differ
// per Bol.com API version/account. BolApiOptions.OffersByEanUrlTemplate is
// configurable precisely so you can correct it without a code change once
// you've checked it against your own API docs/Postman collection — see
// README.md, section "Bol.com integratie". Every call here is wrapped so a
// wrong path or a down API degrades to "price unknown" (fallback offer),
// never a broken booking.
public class BolPriceService
{
    private readonly HttpClient _http;
    private readonly BolApiOptions _opt;
    private readonly ILogger<BolPriceService> _logger;

    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAtUtc = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public BolPriceService(HttpClient http, IOptions<BolApiOptions> opt, ILogger<BolPriceService> logger)
    {
        _http = http;
        _opt = opt.Value;
        _logger = logger;
    }

    // Returns the lowest second-hand offer price found for this EAN/ISBN,
    // or null if it can't be determined (not found, API error, not configured).
    public async Task<decimal?> GetSecondHandPriceAsync(string isbn, CancellationToken ct = default)
    {
        if (!_opt.Enabled || string.IsNullOrWhiteSpace(_opt.ClientId) || string.IsNullOrWhiteSpace(_opt.ClientSecret))
            return null;

        try
        {
            var token = await GetAccessTokenAsync(ct);
            if (token is null) return null;

            var url = string.Format(_opt.OffersByEanUrlTemplate, Uri.EscapeDataString(isbn));
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.retailer.v10+json"));

            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Bol.com offers lookup for {Isbn} failed with {Status}", isbn, res.StatusCode);
                return null;
            }

            using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            return ExtractLowestUsedPrice(doc.RootElement);
        }
        catch (Exception ex)
        {
            // Never let a Bol.com hiccup break a booking — pricing falls back
            // to the fallback offer configured in AppOptions.
            _logger.LogWarning(ex, "Bol.com price lookup failed for {Isbn}", isbn);
            return null;
        }
    }

    // The exact shape of the offers response depends on your API version.
    // This walks a couple of plausible shapes (an "offers" array with a
    // "price" and optional "condition"); adjust here once you've seen a
    // real response for your account.
    private static decimal? ExtractLowestUsedPrice(JsonElement root)
    {
        if (!root.TryGetProperty("offers", out var offers) || offers.ValueKind != JsonValueKind.Array)
            return null;

        decimal? lowest = null;
        foreach (var offer in offers.EnumerateArray())
        {
            var isNew = offer.TryGetProperty("condition", out var cond)
                        && cond.ValueKind == JsonValueKind.String
                        && string.Equals(cond.GetString(), "NEW", StringComparison.OrdinalIgnoreCase);
            if (isNew) continue;

            if (offer.TryGetProperty("price", out var priceEl) && priceEl.TryGetDecimal(out var price))
            {
                if (lowest is null || price < lowest) lowest = price;
            }
        }
        return lowest;
    }

    private async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAtUtc.AddSeconds(-30))
            return _cachedToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAtUtc.AddSeconds(-30))
                return _cachedToken;

            using var req = new HttpRequestMessage(HttpMethod.Post, _opt.TokenUrl);
            var basic = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_opt.ClientId}:{_opt.ClientSecret}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Bol.com token request failed with {Status}", res.StatusCode);
                return null;
            }

            using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var token = doc.RootElement.GetProperty("access_token").GetString();
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 300;

            _cachedToken = token;
            _tokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
