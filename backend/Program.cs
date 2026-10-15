using Backend.Data;
using Backend.Endpoints;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings__Postgres must be configured.");
}

var rateServiceUrl = builder.Configuration["RateService:BaseUrl"];
if (!Uri.TryCreate(rateServiceUrl, UriKind.Absolute, out var rateServiceUri)
    || rateServiceUri.Scheme is not ("http" or "https"))
{
    throw new InvalidOperationException("RateService__BaseUrl must be an absolute HTTP URL.");
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHttpClient<RateServiceClient>(client =>
{
    client.BaseAddress = rateServiceUri;
    client.Timeout = TimeSpan.FromSeconds(25);
});
builder.Services.AddScoped<MarketCatalogService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Currency Converter API", Version = "v1" }));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// This small single-instance lab app applies its checked-in migrations on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithSummary("Проверить работу бекенда")
    .WithDescription("Возвращает статус процесса, если бекенд принимает HTTP-запросы.");
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
    .WithSummary("Проверить готовность бекенда")
    .WithDescription("Проверяет подключение к PostgreSQL. Возвращает HTTP 503, если база недоступна.");

app.MapConversionEndpoints();

await app.RunAsync();
