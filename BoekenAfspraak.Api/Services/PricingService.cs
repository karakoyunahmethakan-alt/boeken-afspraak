using Microsoft.Extensions.Options;

namespace BoekenAfspraak.Api.Services;

public class PricingService
{
    private readonly AppOptions _opt;

    public PricingService(IOptions<AppOptions> opt)
    {
        _opt = opt.Value;
    }

    // "Snel" pad: geen ISBN's ingevuld. Elk vol blok van FlatBlockSize boeken
    // telt voor FlatPricePerBlock euro; een aangebroken blok telt niet mee.
    public decimal CalculateFlatEstimate(int bookCount)
    {
        var fullBlocks = bookCount / _opt.FlatBlockSize;
        return fullBlocks * _opt.FlatPricePerBlock;
    }

    // Per-boek bod op basis van de 2e-hands prijs die bij Bol.com is gevonden.
    // sourcePrice == null betekent: ISBN niet gevonden/geen prijsdata.
    public decimal CalculateIsbnOffer(decimal? sourcePrice)
    {
        if (sourcePrice is null) return _opt.IsbnFallbackOffer;
        return sourcePrice.Value <= _opt.IsbnLowTierMaxSourcePrice
            ? _opt.IsbnLowTierOffer
            : _opt.IsbnHighTierOffer;
    }
}
