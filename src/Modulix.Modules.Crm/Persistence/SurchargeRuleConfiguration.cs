using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modulix.Modules.Crm.Domain;

namespace Modulix.Modules.Crm.Persistence;

internal sealed class SurchargeRuleConfiguration : IEntityTypeConfiguration<SurchargeRule>
{
    public void Configure(EntityTypeBuilder<SurchargeRule> builder)
    {
        CrmTables.ConfigureCommon(builder, "SurchargeRules");
        builder.Property(r => r.Name).HasMaxLength(SurchargeRule.NameMaxLength).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(SurchargeRule.DescriptionMaxLength);
        builder.Property(r => r.Kind).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(Quote.CurrencyLength).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.Property(r => r.Percent).HasPrecision(9, 4);
        builder.Property(r => r.TaxRatePercent).HasPrecision(9, 4);
        builder.Property(r => r.GroupKey).HasMaxLength(SurchargeRule.GroupMaxLength);
        builder.Property(r => r.BaseKey).HasMaxLength(SurchargeRule.KeyMaxLength);
        builder.Property(r => r.DurationKey).HasMaxLength(SurchargeRule.KeyMaxLength);
        builder.Property(r => r.FreeUnits).HasPrecision(18, 6).HasDefaultValue(0m);
        builder.Property(r => r.QuantityKeys).HasMaxLength(SurchargeRule.ListMaxLength);
        builder.Property(r => r.Conditions).HasMaxLength(SurchargeRule.ListMaxLength);
        builder.Property(r => r.Presentation).HasMaxLength(20).IsRequired();
        builder.HasIndex(r => new { r.TenantId, r.IsActive, r.Priority });
        builder.ToTable(t => t.HasCheckConstraint("CK_SurchargeRules_Amount", "[Amount] IS NULL OR [Amount] >= 0"));
        builder.ToTable(t => t.HasCheckConstraint("CK_SurchargeRules_Percent", "[Percent] IS NULL OR ([Percent] >= 0 AND [Percent] <= 100)"));
        builder.ToTable(t => t.HasCheckConstraint("CK_SurchargeRules_TaxRate", "[TaxRatePercent] IS NULL OR ([TaxRatePercent] >= 0 AND [TaxRatePercent] <= 100)"));
        builder.ToTable(t => t.HasCheckConstraint("CK_SurchargeRules_FreeUnits", "[FreeUnits] >= 0"));
    }
}
