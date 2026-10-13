using Backend.Data;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public sealed class MarketCatalogService(RateServiceClient rates, AppDbContext db)
{
    private static readonly SemaphoreSlim SyncGate = new(1, 1);

    public async Task<MarketSnapshot> GetAndSyncAsync(CancellationToken cancellationToken)
    {
        var market = await rates.GetMarketAsync(cancellationToken);
        await SyncGate.WaitAsync(cancellationToken);
        try
        {
            var existing = await db.Currencies.ToDictionaryAsync(x => x.Code, cancellationToken);
            var changed = false;
            foreach (var item in market.Currencies)
            {
                if (existing.TryGetValue(item.Code, out var currency))
                {
                    if (currency.Name != item.Name || currency.Kind != "fiat")
                    {
                        currency.Name = item.Name;
                        currency.Kind = "fiat";
                        changed = true;
                    }
                }
                else
                {
                    db.Currencies.Add(new Currency { Code = item.Code, Name = item.Name, Kind = "fiat" });
                    changed = true;
                }
            }
            if (changed)
                await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            SyncGate.Release();
        }
        return market;
    }
}
