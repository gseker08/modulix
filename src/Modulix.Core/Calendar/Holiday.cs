using Modulix.Core.Contracts.Tenancy;

namespace Modulix.Core.Calendar;

/// <summary>
/// Firmanın resmî tatil ve idari izin günü (ADR-0049 dilim 6a; ADR-0042 dilim 2'de İK modülündeydi). Tarih tenant içinde tektir; yarım gün (arife)
/// yarım gün sayılır. Tenant verisidir: Row-Level Security ile korunur, tenant dışa aktarımına ve silmesine girer. Kayıt boru hattından yazılır
/// (<c>core.holiday</c>); şirketler arasında ortaktır.
/// </summary>
public sealed class Holiday : ITenantEntity, ICompanyShared
{
    public const int NameMaxLength = 150;

    private Holiday()
    {
        // EF Core için
    }

    public Holiday(DateOnly date, string name, bool halfDay)
    {
        Id = Guid.CreateVersion7();
        Date = date;
        Name = name;
        HalfDay = halfDay;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public DateOnly Date { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public bool HalfDay { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long Version { get; private set; }

    public void Change(DateOnly date, string name, bool halfDay)
    {
        Date = date;
        Name = name;
        HalfDay = halfDay;
        UpdatedAt = DateTimeOffset.UtcNow;
        Version++;
    }
}
