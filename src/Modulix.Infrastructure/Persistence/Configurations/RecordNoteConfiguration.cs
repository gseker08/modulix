using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modulix.Core.Notes;

namespace Modulix.Infrastructure.Persistence.Configurations;

internal sealed class RecordNoteConfiguration : IEntityTypeConfiguration<RecordNote>
{
    public void Configure(EntityTypeBuilder<RecordNote> builder)
    {
        // Kayıt notları (ADR-0057): tenant verisi; Row-Level Security ile korunur (migration: TenantIsolationSql).
        builder.ToTable("RecordNotes", DatabaseSchemas.Core);
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.TenantId).IsRequired();
        builder.Property(n => n.ParentEntityKey).HasMaxLength(RecordNote.ParentEntityKeyMaxLength).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(RecordNote.BodyMaxLength).IsRequired();
        builder.Property(n => n.Version).IsRequired().IsConcurrencyToken();
        builder.Property(n => n.CreatedAt).IsRequired();
        builder.Property(n => n.UpdatedAt).IsRequired();

        builder.HasIndex(n => new { n.TenantId, n.ParentEntityKey, n.ParentId, n.CreatedAt });
    }
}
