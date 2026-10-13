using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Conversion> Conversions => Set<Conversion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Currency>(entity =>
        {
            entity.ToTable("currencies");
            entity.HasKey(x => x.Code);
            entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(4);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            entity.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(10).IsRequired();
            entity.HasData(
                new Currency { Code = "RUB", Name = "Russian ruble", Kind = "fiat" },
                new Currency { Code = "GEL", Name = "Georgian lari", Kind = "fiat" },
                new Currency { Code = "USD", Name = "US dollar", Kind = "fiat" },
                new Currency { Code = "EUR", Name = "Euro", Kind = "fiat" });
        });

        modelBuilder.Entity<Conversion>(entity =>
        {
            entity.ToTable("conversions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(x => x.FromCurrency).HasColumnName("from_currency").HasMaxLength(4).IsRequired();
            entity.Property(x => x.ToCurrency).HasColumnName("to_currency").HasMaxLength(4).IsRequired();
            entity.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(38,18)");
            entity.Property(x => x.Rate).HasColumnName("rate").HasColumnType("numeric(38,18)");
            entity.Property(x => x.Result).HasColumnName("result").HasColumnType("numeric(38,18)");
            entity.Property(x => x.RateSource).HasColumnName("rate_source").HasMaxLength(80).IsRequired();
            entity.Property(x => x.RateAsOfUtc).HasColumnName("rate_as_of_utc");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.Note).HasColumnName("note").HasMaxLength(250);
            entity.HasOne<Currency>().WithMany().HasForeignKey(x => x.FromCurrency).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Currency>().WithMany().HasForeignKey(x => x.ToCurrency).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.CreatedAtUtc);
        });
    }
}
