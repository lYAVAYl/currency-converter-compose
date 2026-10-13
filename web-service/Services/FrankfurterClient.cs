using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace WebService.Services;

public sealed record MarketCurrency(string Code, string Name, decimal Rate, DateTimeOffset AsOfUtc);
public sealed record MarketSnapshot(
    string BaseCurrency,
    string Source,
    DateTimeOffset AsOfUtc,
    IReadOnlyList<MarketCurrency> Currencies);

internal sealed record FrankfurterCurrency(
    [property: JsonPropertyName("iso_code")] string IsoCode,
    string Name);

internal sealed record FrankfurterRate(DateOnly Date, string Base, string Quote, decimal Rate);
internal sealed record CachedMarket(MarketSnapshot Value, DateTimeOffset FetchedAtUtc);

public sealed class FrankfurterClient(HttpClient httpClient, IMemoryCache cache)
{
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(10);
    private static readonly HashSet<string> NonMoneyCodes = new(StringComparer.Ordinal)
    {
        "CMD", "XAG", "XAU", "XDR", "XPD", "XPT"
    };

    public async Task<MarketSnapshot> GetMarketAsync(CancellationToken cancellationToken)
    {
        cache.TryGetValue<CachedMarket>("frankfurter:market", out var cached);
        if (cached is not null && DateTimeOffset.UtcNow - cached.FetchedAtUtc < FreshFor)
            return cached.Value;

        try
        {
            using var currenciesResponse = await httpClient.GetAsync("v2/currencies", cancellationToken);
            using var ratesResponse = await httpClient.GetAsync("v2/rates?base=USD", cancellationToken);
            if (!currenciesResponse.IsSuccessStatusCode || !ratesResponse.IsSuccessStatusCode)
                throw new RateProviderException("Frankfurter returned an unsuccessful response.");

            var catalogue = await currenciesResponse.Content.ReadFromJsonAsync<FrankfurterCurrency[]>(cancellationToken)
                ?? throw new RateProviderException("Frankfurter returned an empty currency list.");
            var rates = await ratesResponse.Content.ReadFromJsonAsync<FrankfurterRate[]>(cancellationToken)
                ?? throw new RateProviderException("Frankfurter returned an empty rate list.");

            var rateByCode = rates
                .Where(x => x.Rate > 0 && x.Date != default
                    && string.Equals(x.Base, "USD", StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => x.Quote.ToUpperInvariant(), StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.OrderByDescending(rate => rate.Date).First(), StringComparer.Ordinal);

            var currencies = catalogue
                .Where(x => x.IsoCode is not null && x.Name is not null)
                .Select(x => new { Code = x.IsoCode.ToUpperInvariant(), x.Name })
                .Where(x => x.Code.Length == 3 && x.Code.All(char.IsAsciiLetterUpper)
                    && !NonMoneyCodes.Contains(x.Code)
                    && !string.IsNullOrWhiteSpace(x.Name) && rateByCode.ContainsKey(x.Code))
                .DistinctBy(x => x.Code, StringComparer.Ordinal)
                .Select(x => new MarketCurrency(
                    x.Code,
                    x.Name.Trim(),
                    rateByCode[x.Code].Rate,
                    AsUtc(rateByCode[x.Code].Date)))
                .OrderBy(x => x.Code, StringComparer.Ordinal)
                .ToArray();

            if (currencies.Length < 30 || !currencies.Any(x => x.Code == "USD")
                || !currencies.Any(x => x.Code == "RUB") || !currencies.Any(x => x.Code == "GEL"))
                throw new RateProviderException("Frankfurter returned an incomplete rate list.");

            var market = new MarketSnapshot("USD", "Frankfurter", currencies.Min(x => x.AsOfUtc), currencies);
            cache.Set("frankfurter:market", new CachedMarket(market, DateTimeOffset.UtcNow), TimeSpan.FromDays(2));
            return market;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is RateProviderException or HttpRequestException
            or JsonException or TaskCanceledException or InvalidOperationException)
        {
            if (cached is not null)
                return cached.Value;
            throw new RateProviderException("Frankfurter is unavailable or returned invalid data.");
        }
    }

    private static DateTimeOffset AsUtc(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
}
