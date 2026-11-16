using System.Globalization;

namespace Modulix.Core.Audit;

/// <summary>
/// Arşivlenmiş denetim kaydı dönemi (ADR-0052 dilim 3). <see cref="PeriodStart"/> ilk bölümde boştur (alt sınır yok); <see cref="PeriodEnd"/>
/// hariçtir ve bir ayın ilk günü 00:00 UTC'dir. <see cref="Sha256"/> sıkıştırılmış dosyanın özetidir; indiren bütünlüğü bununla denetler.
/// </summary>
public sealed record AuditArchiveItem(
    Guid Id, DateTimeOffset? PeriodStart, DateTimeOffset PeriodEnd, long RowCount, long SizeBytes, string Sha256, DateTimeOffset ArchivedAt)
{
    /// <summary>Dönemin ilk ayı (<c>yyyy-MM</c>); alt sınırı olmayan ilk bölümde boş.</summary>
    public string? FirstMonth => PeriodStart?.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Dönemin son ayı (<c>yyyy-MM</c>).</summary>
    public string LastMonth => PeriodEnd.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Okunabilir dönem: tek ay "2026-01", birden çok ay "2026-01 – 2026-03", alt sınırı olmayan ilk bölüm "… – 2026-03".</summary>
    public string PeriodText => FirstMonth == LastMonth ? LastMonth : $"{FirstMonth ?? "…"} – {LastMonth}";
}

/// <summary>Açılmış arşiv dosyası (gzip'li JSON Lines); çağıran akışı kapatır.</summary>
public sealed record AuditArchiveDownload(AuditArchiveItem Archive, Stream Content);

/// <summary>
/// Aktif tenant'ın denetim kaydı arşivi (ADR-0052 dilim 3): sıcak saklama süresinden eski aylar ekranda aranmaz, dosya olarak indirilir.
/// Arşivi yalnızca <c>audit-archive</c> komutu yazar; burada okuma ve indirme vardır. İndirme denetim kaydına yazılır.
/// </summary>
public interface IAuditArchives
{
    /// <summary>Arşivlenmiş dönemler, en yeni önce.</summary>
    Task<IReadOnlyList<AuditArchiveItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Dönemin dosyasını açar ve indirmeyi denetim kaydına yazar. Dönem başka tenant'ınsa ya da yoksa, dosya depoda bulunmazsa <c>null</c>
    /// (o zaman denetim kaydı yazılmaz).
    /// </summary>
    Task<AuditArchiveDownload?> OpenAsync(Guid id, CancellationToken cancellationToken);
}
