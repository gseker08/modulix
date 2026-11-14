using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modulix.Core.Numbering;

namespace Modulix.Infrastructure.Persistence.Configurations;

internal sealed class NumberCounterConfiguration : IEntityTypeConfiguration<NumberCounter>
{
    public void Configure(EntityTypeBuilder<NumberCounter> builder)
    {
        // Belge serisi sayacı (ADR-0049): tenant verisi; Row-Level Security ile korunur (migration: TenantIsolationSql).
        builder.ToTable("NumberCounters", DatabaseSchemas.Core);
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.SeriesKey).HasMaxLength(NumberCounter.SeriesKeyMaxLength).IsRequired();
        builder.Property(c => c.Period).IsRequired();
        builder.Property(c => c.LastValue).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();

        // Seri ve dönem başına tek sayaç; numaralama servisi satırı bu dizinle bulur ve kilitler.
        builder.HasIndex(c => new { c.TenantId, c.SeriesKey, c.Period }).IsUnique();
    }
}
