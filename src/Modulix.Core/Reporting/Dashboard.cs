using Modulix.Core.Contracts.Tenancy;

namespace Modulix.Core.Reporting;

/// <summary>Pano bileşeninin gösterim türü.</summary>
public enum DashboardWidgetKind
{
    /// <summary>Tek sayı: özet raporun genel toplamı (ilk ölçü), tablo raporunda eşleşen kayıt sayısı.</summary>
    Number = 1,

    /// <summary>Çubuk grafik: özet raporun grupları ve ilk ölçüsü. Tablo raporuna bağlanamaz.</summary>
    Bar = 2,
}

/// <summary>
/// Kullanıcının kurduğu pano (CRM epik E2, dilim 1): kayıtlı raporlara bağlı bileşenlerin sıralı listesi. Pano <b>veri erişimi vermez</b>:
/// her bileşen, bakan kişinin yetkisiyle (kayıt izni, kapsam, hassas alan) çalışır. Bu dilimde pano yalnızca sahibine görünür; paylaşım
/// sonraki dilimdedir. Tenant verisidir; Row-Level Security ile korunur.
/// </summary>
public sealed class Dashboard : ITenantEntity, ICompanyShared
{
    public const int NameMaxLength = 120;

    /// <summary>Bir kullanıcının sahip olabileceği en fazla pano.</summary>
    public const int MaxPerUser = 20;

    /// <summary>Bir panodaki en fazla bileşen (her bileşen bir rapor çalıştırır).</summary>
    public const int MaxWidgets = 12;

    private Dashboard()
    {
        // EF Core için
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Eşzamanlılık belirteci: her değişiklikte bir artar.</summary>
    public long Version { get; private set; }

    public static Dashboard Create(Guid ownerUserId, string name, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OwnerUserId = ownerUserId,
            Name = NormalizeName(name),
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

    public void Update(string name, DateTimeOffset now)
    {
        Name = NormalizeName(name);
        UpdatedAt = now;
        Version++;
    }

    public static string NormalizeName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > NameMaxLength)
        {
            throw new ArgumentException($"Pano adı 1-{NameMaxLength} karakter olmalı.", nameof(name));
        }

        return trimmed;
    }
}

/// <summary>Panodaki bir bileşen: kayıtlı rapor + gösterim türü. Rapor silinince bileşen de silinir.</summary>
public sealed class DashboardWidget : ITenantEntity, ICompanyShared
{
    public const int TitleMaxLength = 120;

    private DashboardWidget()
    {
        // EF Core için
    }

    public DashboardWidget(Guid dashboardId, Guid reportId, DashboardWidgetKind kind, string? title, int position)
    {
        Id = Guid.CreateVersion7();
        DashboardId = dashboardId;
        ReportId = reportId;
        Kind = kind;
        Title = NormalizeTitle(title);
        Position = position;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid DashboardId { get; private set; }

    public Guid ReportId { get; private set; }

    public DashboardWidgetKind Kind { get; private set; }

    /// <summary>Boşsa raporun adı gösterilir.</summary>
    public string? Title { get; private set; }

    public int Position { get; private set; }

    public static string? NormalizeTitle(string? title)
    {
        var trimmed = title?.Trim();
        if (trimmed is { Length: > TitleMaxLength })
        {
            throw new ArgumentException($"Bileşen başlığı en fazla {TitleMaxLength} karakter olabilir.", nameof(title));
        }

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

public sealed record DashboardWidgetInput(Guid ReportId, DashboardWidgetKind Kind, string? Title);

public sealed record DashboardInput(string Name, IReadOnlyList<DashboardWidgetInput> Widgets);

/// <summary><see cref="ReportName"/>: bakan kişi raporu artık göremiyorsa null (bileşen veri göstermez).</summary>
public sealed record DashboardWidgetView(Guid Id, Guid ReportId, string? ReportName, DashboardWidgetKind Kind, string? Title, int Position);

public sealed record DashboardSummary(Guid Id, string Name, int WidgetCount, DateTimeOffset UpdatedAt, long Version);

public sealed record DashboardDetail(DashboardSummary Summary, IReadOnlyList<DashboardWidgetView> Widgets);

/// <summary>Girdi geçersiz (ad, sınır, bileşen). <see cref="Field"/> alan hatası olarak döner.</summary>
public sealed class DashboardException(string message, string field = "name") : Exception(message)
{
    public string Field { get; } = field;
}

/// <summary>Pano başka bir sekmede değiştirildi (sürüm uyuşmuyor). API 409.</summary>
public sealed class DashboardConflictException(string message) : Exception(message);

/// <summary>
/// Aktif tenant'ta çağıranın panoları (CRM epik E2, dilim 1). Pano yalnızca sahibine görünür; başkasının ya da başka firmanın panosu
/// "bulunamadı"dır. Yazma işlemleri <c>core.dashboard</c> kaydı olarak denetime yazılır. Yetki (<c>core.reports.*</c>) uç noktada denetlenir.
/// </summary>
public interface IDashboards
{
    Task<IReadOnlyList<DashboardSummary>> ListAsync(CancellationToken cancellationToken);

    Task<DashboardDetail?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<DashboardDetail> CreateAsync(DashboardInput input, CancellationToken cancellationToken);

    /// <summary>Adı ve bileşen listesini bütün olarak değiştirir (sıra listedeki sıradır).</summary>
    Task<DashboardDetail?> UpdateAsync(Guid id, DashboardInput input, long? expectedVersion, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Bileşenin verisi: bağlı rapor çağıranın yetkisiyle çalıştırılır. Pano ya da bileşen yoksa, ya da çağıran raporu göremiyorsa null.
    /// </summary>
    Task<ReportResult?> RunWidgetAsync(Guid id, Guid widgetId, CancellationToken cancellationToken);
}
