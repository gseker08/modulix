using Modulix.Core.Contracts.Records;

namespace Modulix.Core.Contracts.Reporting;

/// <summary>Rapor merkezinin izin anahtarları (Core'a aittir; modül uç noktası kendi raporlarını dışa aktarırken bunları ister, ADR-0046).</summary>
public static class ReportPermissions
{
    /// <summary>Rapor merkezini açar, hazır ve paylaşılan raporları çalıştırır (ADR-0035). Veri erişimi vermez.</summary>
    public const string Read = "core.reports.read";

    /// <summary>Rapor sonucunu dosya olarak dışa aktarır (ADR-0035, dilim 4).</summary>
    public const string Export = "core.reports.export";
}

public enum ReportTableFormat
{
    Csv = 1,
    Xlsx = 2,
}

/// <summary>Dışa aktarılacak tablonun sütunu.</summary>
public sealed record ReportTableColumn(string Key, string Label, DatasetColumnKind Kind);

/// <summary>
/// Modülün kendi hesapladığı hazır tablo (ör. satış kartı ayrıntısı). <see cref="DatasetKey"/> dosyanın erişim kuralını taşır: çağıran o veri kümesini
/// okuyamıyorsa dosya hazırlanmaz, indirme anında da yeniden denetlenir. Hücreler sütun sırasıyla verilir (sayı, metin, "yyyy-MM-dd" tarih).
/// </summary>
/// <param name="Name">Dosyanın ve dışa aktarma kaydının adı.</param>
/// <param name="Title">Dosyadaki başlık (ör. kart adı).</param>
public sealed record ReportTable(
    string Name,
    string DatasetKey,
    string Title,
    IReadOnlyList<ReportTableColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    bool Truncated,
    DateTimeOffset GeneratedAt);

/// <summary>Hazırlanan dosyanın üst bilgisi (içerik yok). <see cref="Source"/> camelCase adıdır (ör. "manual").</summary>
public sealed record ReportTableExport(
    Guid Id,
    Guid? ReportId,
    string ReportName,
    string DatasetKey,
    string FileName,
    long SizeBytes,
    int RowCount,
    bool Truncated,
    string Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    int DownloadCount,
    bool Expired);

/// <summary>
/// Modülün hazır tablosunu rapor merkezinin dışa aktarma dosyasına çevirir (ADR-0035 dilim 4 kuralları: sahibine özel, 24 saat, kullanıcı başına sınır,
/// denetim; ADR-0046: modül uç noktası Core'un rapor servisini doğrudan görmez). İçerik çağıranın yetkisiyle üretilmiş olmalıdır; burada yeniden hesap yoktur.
/// Hatalar <see cref="RecordResult{T}"/> olarak döner: rapor kuralı ihlali doğrulama, yetki yokluğu izin, sınır/çakışma çakışma hatasıdır.
/// </summary>
public interface IReportTableExports
{
    Task<RecordResult<ReportTableExport>> CreateAsync(ReportTable table, ReportTableFormat format, CancellationToken cancellationToken);
}
