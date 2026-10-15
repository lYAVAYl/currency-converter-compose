using WebService.Services;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
var frankfurterUri = ProviderUri(builder.Configuration["Providers:FrankfurterBaseUrl"], "Frankfurter");

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<FrankfurterClient>(client =>
{
    client.BaseAddress = frankfurterUri;
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddTransient<RateCalculator>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Exchange Rates API", Version = "v1" }));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithSummary("Проверить работу сервиса курсов")
    .WithDescription("Возвращает статус процесса без обращения к внешнему поставщику курсов.");
app.MapGet("/api/market", async (FrankfurterClient frankfurter, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await frankfurter.GetMarketAsync(cancellationToken));
    }
    catch (RateProviderException)
    {
        return Results.Problem("Could not obtain exchange rates from Frankfurter.", statusCode: 503);
    }
})
    .WithSummary("Получить валютный рынок")
    .WithDescription("Возвращает список денежных валют и их курсы относительно USD из Frankfurter. При недоступности данных возвращает HTTP 503.");

app.MapGet("/api/rates", async (
    string? from,
    string? to,
    RateCalculator calculator,
    CancellationToken cancellationToken) =>
{
    var source = from?.Trim().ToUpperInvariant();
    var target = to?.Trim().ToUpperInvariant();
    var errors = new Dictionary<string, string[]>();

    if (source is null || source.Length != 3 || !source.All(char.IsAsciiLetterUpper))
        errors["from"] = ["Choose a three-letter currency code."];
    if (target is null || target.Length != 3 || !target.All(char.IsAsciiLetterUpper))
        errors["to"] = ["Choose a three-letter currency code."];
    if (source == target && source is not null)
        errors["to"] = ["Choose a different target currency."];
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    try
    {
        var quote = await calculator.GetRateAsync(source!, target!, cancellationToken);
        return Results.Ok(quote);
    }
    catch (UnsupportedCurrencyException)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["currency"] = ["Currency is not available in current rates."]
        });
    }
    catch (RateProviderException)
    {
        return Results.Problem("Could not obtain exchange rates from Frankfurter.", statusCode: 503);
    }
})
    .WithSummary("Получить курс валютной пары")
    .WithDescription("Возвращает курс из валюты from в валюту to. Оба параметра — разные трёхбуквенные коды; при ошибке валидации возвращает HTTP 400.");

await app.RunAsync();

static Uri ProviderUri(string? value, string provider)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        throw new InvalidOperationException($"Providers__{provider}BaseUrl must be an absolute HTTP URL.");
    return uri;
}
