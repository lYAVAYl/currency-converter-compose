namespace Backend.Models;

public sealed class Currency
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Kind { get; set; }
}
