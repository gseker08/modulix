using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modulix.Modules.Crm.Domain;

namespace Modulix.Modules.Crm.Persistence;

internal sealed class LeadScoreRuleConfiguration : IEntityTypeConfiguration<LeadScoreRule>
{
    public void Configure(EntityTypeBuilder<LeadScoreRule> builder)
    {
        CrmTables.ConfigureCommon(builder, "LeadScoreRules");
        builder.Property(r => r.Name).HasMaxLength(LeadScoreRule.NameMaxLength).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(LeadScoreRule.DescriptionMaxLength);
        builder.Property(r => r.Field).HasMaxLength(LeadScoreRule.FieldMaxLength).IsUnicode(false).IsRequired();
        builder.Property(r => r.Operator).HasMaxLength(LeadScoreRule.OperatorMaxLength).IsUnicode(false).IsRequired();
        builder.Property(r => r.Value).HasMaxLength(LeadScoreRule.ValueMaxLength);
        builder.HasIndex(r => new { r.TenantId, r.IsActive });
        builder.ToTable(t => t.HasCheckConstraint("CK_LeadScoreRules_Points", "[Points] BETWEEN -100 AND 100 AND [Points] <> 0"));
    }
}
