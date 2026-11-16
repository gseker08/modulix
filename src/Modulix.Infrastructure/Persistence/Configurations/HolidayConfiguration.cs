using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modulix.Core.Calendar;

namespace Modulix.Infrastructure.Persistence.Configurations;

internal sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> builder)
    {
        // Firmanın tatil listesi (ADR-0049 dilim 6a): tenant verisi; Row-Level Security ile korunur (migration: TenantIsolationSql).
        builder.ToTable("Holidays", DatabaseSchemas.Core);
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.TenantId).IsRequired();
        builder.Property(h => h.Name).HasMaxLength(Holiday.NameMaxLength).IsRequired();
        builder.Property(h => h.Version).IsRequired().IsConcurrencyToken();
        builder.Property(h => h.CreatedAt).IsRequired();
        builder.Property(h => h.UpdatedAt).IsRequired();

        // Tarih tenant içinde tektir.
        builder.HasIndex(h => new { h.TenantId, h.Date }).IsUnique();
    }
}
