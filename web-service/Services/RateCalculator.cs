namespace WebService.Services;

public sealed record RateQuote(
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    DateTimeOffset AsOfUtc,
    string Source);

public sealed class UnsupportedCurrencyException() : Exception("Currency is not available in current rates.");

public sealed class RateCalculator(FrankfurterClient frankfurter)
{
    public async Task<RateQuote> GetRateAsync(string from, string to, CancellationToken cancellationToken)
    {
        var market = await frankfurter.GetMarketAsync(cancellationToken);
        var source = market.Currencies.FirstOrDefault(x => x.Code == from);
        var target = market.Currencies.FirstOrDefault(x => x.Code == to);
        if (source is null || target is null)
            throw new UnsupportedCurrencyException();

        decimal rate;
        try
        {
            rate = target.Rate / source.Rate;
        }
        catch (OverflowException)
        {
            throw new RateProviderException("Calculated exchange rate is too large.");
        }

        return new RateQuote(from, to, rate,
            source.AsOfUtc < target.AsOfUtc ? source.AsOfUtc : target.AsOfUtc,
            market.Source);
    }
}
