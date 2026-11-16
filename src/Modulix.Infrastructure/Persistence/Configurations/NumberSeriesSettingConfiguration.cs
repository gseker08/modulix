using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modulix.Core.Contracts.Numbering;
using Modulix.Core.Numbering;

namespace Modulix.Infrastructure.Persistence.Configurations;

internal sealed class NumberSeriesSettingConfiguration : IEntityTypeConfiguration<NumberSeriesSetting>
{
    public void Configure(EntityTypeBuilder<NumberSeriesSetting> builder)
    {
        // Firmanın belge serisi yapılandırması (ADR-0049 dilim 2a): tenant verisi; Row-Level Security ile korunur (migration: TenantIsolationSql).
        builder.ToTable("NumberSeriesSettings", DatabaseSchemas.Core);
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.SeriesKey).HasMaxLength(NumberSeriesDefinition.KeyMaxLength).IsRequired();
        builder.Property(s => s.Prefix).HasMaxLength(NumberSeriesDefinition.PrefixMaxLength);
        builder.Property(s => s.Version).IsConcurrencyToken();
        builder.Ignore(s => s.IsCustomized);

        // Seri başına tek yapılandırma.
        builder.HasIndex(s => new { s.TenantId, s.SeriesKey }).IsUnique();
    }
}
