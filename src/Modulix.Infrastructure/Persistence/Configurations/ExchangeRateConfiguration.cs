using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modulix.Core.Currency;

namespace Modulix.Infrastructure.Persistence.Configurations;

internal sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        // Firmanın döviz kurları (ADR-0049 dilim 5a): tenant verisi; Row-Level Security ile korunur (migration: TenantIsolationSql).
        builder.ToTable("ExchangeRates", DatabaseSchemas.Core, t =>
        {
            t.HasCheckConstraint("CK_ExchangeRates_Rate", "[Rate] > 0");
            t.HasCheckConstraint("CK_ExchangeRates_Pair", "[FromCurrency] <> [ToCurrency]");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.FromCurrency).HasMaxLength(ExchangeRate.CurrencyLength).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(r => r.ToCurrency).HasMaxLength(ExchangeRate.CurrencyLength).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(r => r.Rate).HasPrecision(28, 10);
        builder.Property(r => r.ValidOn).HasColumnType("date");
        builder.Property(r => r.Source).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Note).HasMaxLength(ExchangeRate.NoteMaxLength);
        builder.Property(r => r.Version).IsRequired().IsConcurrencyToken();
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.UpdatedAt).IsRequired();

        // Aynı çift ve gün için tek kayıt.
        builder.HasIndex(r => new { r.TenantId, r.FromCurrency, r.ToCurrency, r.ValidOn }).IsUnique().HasDatabaseName("UX_ExchangeRates_PairDay");
    }
}
