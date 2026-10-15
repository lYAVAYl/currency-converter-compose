namespace Backend.Models;

public sealed class Conversion
{
    public long Id { get; set; }
    public required string FromCurrency { get; set; }
    public required string ToCurrency { get; set; }
    public decimal Amount { get; set; }
    public decimal Rate { get; set; }
    public decimal Result { get; set; }
    public required string RateSource { get; set; }
    public DateTimeOffset RateAsOfUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
