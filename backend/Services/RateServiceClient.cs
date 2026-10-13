using System.Net.Http.Json;
using System.Text.Json;

namespace Backend.Services;

public sealed record MarketCurrency(string Code, string Name, decimal Rate, DateTimeOffset AsOfUtc);
public sealed record MarketSnapshot(
    string BaseCurrency,
    string Source,
    DateTimeOffset AsOfUtc,
    IReadOnlyList<MarketCurrency> Currencies);

public sealed class RateServiceException(string message) : Exception(message);

public sealed class RateServiceClient(HttpClient httpClient)
{
    public async Task<MarketSnapshot> GetMarketAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("api/market", cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new RateServiceException($"Rate service returned HTTP {(int)response.StatusCode}.");

            var market = await response.Content.ReadFromJsonAsync<MarketSnapshot>(cancellationToken);
            if (market is null || market.BaseCurrency != "USD" || market.Source != "Frankfurter"
                || market.Currencies is null || market.Currencies.Count < 30
                || market.Currencies.Any(x => x is null || x.Code is null || x.Name is null
                    || x.Code.Length != 3 || x.Name.Length is < 1 or > 80
                    || x.Rate <= 0 || x.AsOfUtc == default)
                || market.Currencies.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != market.Currencies.Count)
                throw new RateServiceException("Rate service returned an invalid market snapshot.");

            return market;
        }
        catch (RateServiceException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw new RateServiceException("Rate service is unavailable or returned invalid data.");
        }
        catch (JsonException)
        {
            throw new RateServiceException("Rate service is unavailable or returned invalid data.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RateServiceException("Rate service timed out.");
        }
    }

}
