using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using WebService.Services;

var codes = new List<string> { "USD", "RUB", "GEL", "EUR" };
for (var index = 0; codes.Count < 120; index++)
    codes.Add($"A{(char)('A' + index / 26)}{(char)('A' + index % 26)}");
codes.Add("XAU");

var catalogue = codes.Select(code => new { iso_code = code, name = $"Currency {code}" });
var rates = codes.Select(code => new
{
    date = "2026-10-13",
    @base = "USD",
    quote = code,
    rate = code switch { "RUB" => 90m, "GEL" => 2.7m, _ => 1m }
});

var handler = new StubHandler(new Dictionary<string, string>
{
    ["/v2/currencies"] = JsonSerializer.Serialize(catalogue),
    ["/v2/rates?base=USD"] = JsonSerializer.Serialize(rates)
});
using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
using var cache = new MemoryCache(new MemoryCacheOptions());
var frankfurter = new FrankfurterClient(httpClient, cache);
var market = await frankfurter.GetMarketAsync(CancellationToken.None);
if (market.Currencies.Count != 120 || market.Currencies.All(x => x.Code != "RUB")
    || market.Currencies.All(x => x.Code != "GEL") || market.Currencies.Any(x => x.Code == "XAU"))
    throw new Exception("The active currency catalogue was not parsed.");

var quote = await new RateCalculator(frankfurter).GetRateAsync("RUB", "GEL", CancellationToken.None);
if (quote.Rate != .03m || quote.Source != "Frankfurter" || handler.RequestCount != 2)
    throw new Exception("Cross conversion or market caching failed.");

Console.WriteLine("Market smoke test passed: 120 fiat currencies, RUB → GEL = 0.03, cached rates.");

internal sealed class StubHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        var path = request.RequestUri!.PathAndQuery;
        if (!responses.TryGetValue(path, out var body))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
