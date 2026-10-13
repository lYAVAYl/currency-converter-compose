using Backend.Data;
using Backend.Models;
using Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace Backend.Endpoints;

public sealed record CreateConversionRequest(
    decimal Amount,
    string? FromCurrency,
    string? ToCurrency,
    string? Note);

public sealed record CreateBatchRequest(
    decimal Amount,
    string? FromCurrency,
    string[]? ToCurrencies,
    string? Note);

public sealed record UpdateNoteRequest(string? Note);

public sealed record ConversionResponse(
    long Id,
    string FromCurrency,
    string ToCurrency,
    decimal Amount,
    decimal Rate,
    decimal Result,
    string RateSource,
    DateTimeOffset RateAsOfUtc,
    DateTimeOffset CreatedAtUtc,
    string? Note)
{
    public static ConversionResponse FromEntity(Conversion value) => new(
        value.Id, value.FromCurrency, value.ToCurrency, value.Amount, value.Rate,
        value.Result, value.RateSource, value.RateAsOfUtc, value.CreatedAtUtc, value.Note);
}

public static class ConversionEndpoints
{
    private const decimal MaxAmount = 1_000_000_000_000m;
    private const decimal MaxStoredValue = 99_999_999_999_999_999_999m;

    public static void MapConversionEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/market", async (MarketCatalogService catalogue, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await catalogue.GetAndSyncAsync(cancellationToken));
            }
            catch (RateServiceException)
            {
                return Results.Problem("Currency rates are temporarily unavailable.", statusCode: 503);
            }
        });

        api.MapGet("/currencies", async (MarketCatalogService catalogue, CancellationToken cancellationToken) =>
        {
            try
            {
                var market = await catalogue.GetAndSyncAsync(cancellationToken);
                return Results.Ok(market.Currencies.Select(x => new { x.Code, x.Name, Kind = "fiat" }));
            }
            catch (RateServiceException)
            {
                return Results.Problem("Currency catalogue is temporarily unavailable.", statusCode: 503);
            }
        });

        api.MapGet("/conversions", async (AppDbContext db, int? limit, CancellationToken cancellationToken) =>
        {
            if (limit is < 1 or > 100)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["limit"] = ["Use a limit from 1 to 100."]
                });

            var records = await db.Conversions.AsNoTracking()
                .Where(x => db.Currencies.Any(c => c.Code == x.FromCurrency && c.Kind == "fiat")
                    && db.Currencies.Any(c => c.Code == x.ToCurrency && c.Kind == "fiat"))
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
                .Take(limit ?? 50)
                .ToListAsync(cancellationToken);
            return Results.Ok(records.Select(ConversionResponse.FromEntity));
        });

        api.MapGet("/conversions/{id:long}", async (long id, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var record = await db.Conversions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            return record is null ? Results.NotFound() : Results.Ok(ConversionResponse.FromEntity(record));
        });

        api.MapPost("/conversions", async (
            CreateConversionRequest request,
            AppDbContext db,
            MarketCatalogService catalogue,
            CancellationToken cancellationToken) =>
        {
            var result = await SaveConversionsAsync(request.Amount, request.FromCurrency,
                request.ToCurrency is null ? null : [request.ToCurrency], request.Note,
                db, catalogue, cancellationToken);
            if (result.Error is not null) return result.Error;
            var conversion = result.Records![0];
            return Results.Created($"/api/conversions/{conversion.Id}", ConversionResponse.FromEntity(conversion));
        });

        api.MapPost("/conversions/batch", async (
            CreateBatchRequest request,
            AppDbContext db,
            MarketCatalogService catalogue,
            CancellationToken cancellationToken) =>
        {
            var result = await SaveConversionsAsync(request.Amount, request.FromCurrency,
                request.ToCurrencies, request.Note, db, catalogue, cancellationToken);
            return result.Error ?? Results.Ok(result.Records!.Select(ConversionResponse.FromEntity));
        });

        api.MapPatch("/conversions/{id:long}/note", async (
            long id, UpdateNoteRequest request, AppDbContext db, CancellationToken cancellationToken) =>
        {
            if (request.Note?.Trim().Length > 250)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["note"] = ["Note must be at most 250 characters."]
                });

            var record = await db.Conversions.FindAsync([id], cancellationToken);
            if (record is null)
                return Results.NotFound();

            record.Note = NormalizeNote(request.Note);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ConversionResponse.FromEntity(record));
        });

        api.MapDelete("/conversions/{id:long}", async (long id, AppDbContext db, CancellationToken cancellationToken) =>
            await db.Conversions.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken) == 0
                ? Results.NotFound()
                : Results.NoContent());
    }

    private static async Task<(List<Conversion>? Records, IResult? Error)> SaveConversionsAsync(
        decimal amount,
        string? fromCurrency,
        string[]? toCurrencies,
        string? note,
        AppDbContext db,
        MarketCatalogService catalogue,
        CancellationToken cancellationToken)
    {
        var from = fromCurrency?.Trim().ToUpperInvariant();
        var targets = toCurrencies?.Select(x => x?.Trim().ToUpperInvariant()).ToArray();
        var errors = new Dictionary<string, string[]>();

        if (amount <= 0 || amount > MaxAmount)
            errors["amount"] = ["Amount must be greater than zero and at most 1,000,000,000,000."];
        if (!ValidCode(from))
            errors["fromCurrency"] = ["Choose a supported source currency."];
        if (targets is null || targets.Length is < 1 or > 10 || targets.Any(x => !ValidCode(x))
            || targets.Distinct(StringComparer.Ordinal).Count() != targets.Length
            || targets.Contains(from, StringComparer.Ordinal))
            errors["toCurrencies"] = ["Choose 1 to 10 different target currencies, excluding the source."];
        if (note?.Trim().Length > 250)
            errors["note"] = ["Note must be at most 250 characters."];
        if (errors.Count > 0)
            return (null, Results.ValidationProblem(errors));

        MarketSnapshot market;
        try
        {
            market = await catalogue.GetAndSyncAsync(cancellationToken);
        }
        catch (RateServiceException)
        {
            return (null, Results.Problem("Currency rates are temporarily unavailable.", statusCode: 503));
        }

        var currencies = market.Currencies.ToDictionary(x => x.Code, StringComparer.Ordinal);
        if (!currencies.TryGetValue(from!, out var source)
            || targets!.Any(x => !currencies.ContainsKey(x!)))
            return (null, Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["currency"] = ["Currency is not available in current rates."]
            }));

        var records = new List<Conversion>(targets!.Length);
        foreach (var to in targets)
        {
            var target = currencies[to!];
            decimal rate;
            decimal result;
            try
            {
                rate = decimal.Round(target.Rate / source.Rate, 18, MidpointRounding.ToEven);
                result = decimal.Round(amount * rate, 18, MidpointRounding.ToEven);
            }
            catch (OverflowException)
            {
                return (null, Results.Problem("Calculated amount is too large.", statusCode: 502));
            }
            if (rate <= 0 || rate > MaxStoredValue || result > MaxStoredValue)
                return (null, Results.Problem("Calculated amount is outside supported precision.", statusCode: 502));
            if (result == 0)
                return (null, Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["amount"] = ["Amount is too small for the supported precision."]
                }));

            records.Add(new Conversion
            {
                FromCurrency = from!,
                ToCurrency = to!,
                Amount = amount,
                Rate = rate,
                Result = result,
                RateSource = market.Source,
                RateAsOfUtc = source.AsOfUtc < target.AsOfUtc ? source.AsOfUtc : target.AsOfUtc,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Note = NormalizeNote(note)
            });
        }

        db.Conversions.AddRange(records);
        await db.SaveChangesAsync(cancellationToken);
        return (records, null);
    }

    private static bool ValidCode(string? code) =>
        code is { Length: 3 } && code.All(char.IsAsciiLetterUpper);

    private static string? NormalizeNote(string? note)
    {
        var normalized = note?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
