using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Files;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Audit;

/// <summary>
/// Arşivlenmiş denetim kaydı dönemi (core.AuditArchives, ADR-0052 dilim 2): bir tenant'ın bir bölüm aralığındaki satırları nesne deposunda
/// gzip'li JSON Lines dosyasıdır (<see cref="StorageKey"/>). Satırı yalnızca arşiv komutu (migrator) yazar; uygulama rolü okur, ekleyemez,
/// değiştiremez, silemez. Satır dosyanın tek dayanağıdır: yetim içerik temizliği bu anahtarları da bilinen sayar.
/// </summary>
public sealed class AuditArchiveEntry : ITenantEntity, ICompanyShared
{
    public const int Sha256Length = 64;

    private AuditArchiveEntry()
    {
        // EF Core için
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>Dönemin başı (dahil). İlk bölümde alt sınır yoktur; o zaman <see cref="DateTimeOffset.MinValue"/>.</summary>
    public DateTimeOffset PeriodStart { get; private set; }

    /// <summary>Dönemin sonu (hariç): bölümün üst sınırı, bir ayın ilk günü 00:00 UTC.</summary>
    public DateTimeOffset PeriodEnd { get; private set; }

    public string StorageKey { get; private set; } = string.Empty;

    public long RowCount { get; private set; }

    public long SizeBytes { get; private set; }

    /// <summary>Sıkıştırılmış dosyanın SHA-256 özeti (küçük harf onaltılık); indirmede bütünlük denetimi.</summary>
    public string Sha256 { get; private set; } = string.Empty;

    public DateTimeOffset ArchivedAt { get; private set; }
}

internal sealed class AuditArchiveEntryConfiguration : IEntityTypeConfiguration<AuditArchiveEntry>
{
    public void Configure(EntityTypeBuilder<AuditArchiveEntry> builder)
    {
        builder.ToTable("AuditArchives", DatabaseSchemas.Core);
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.StorageKey).HasMaxLength(StoredFile.StorageKeyLength).IsUnicode(false).IsRequired();
        builder.Property(a => a.Sha256).HasMaxLength(AuditArchiveEntry.Sha256Length).IsUnicode(false).IsRequired();
        builder.HasIndex(a => new { a.TenantId, a.PeriodStart }).IsUnique();
    }
}

/// <summary>Denetim kaydı arşivi ayarları (<c>AuditArchive</c> bölümü, ADR-0052).</summary>
public sealed class AuditArchiveOptions
{
    public const string SectionName = "AuditArchive";

    /// <summary>Denetim kaydı ekranında aranabilir kalan ay sayısı; daha eski kapalı aylar arşivlenir. Varsayılan 24, en az 1.</summary>
    public int HotMonths { get; set; } = 24;

    /// <summary>Komut, bugünden en az bu kadar ay ileriye bölüm sınırı açık tutar. Varsayılan 24.</summary>
    public int FutureMonths { get; set; } = 24;
}
