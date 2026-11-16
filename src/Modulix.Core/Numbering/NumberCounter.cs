using Modulix.Core.Contracts.Numbering;
using Modulix.Core.Contracts.Tenancy;

namespace Modulix.Core.Numbering;

/// <summary>
/// Bir belge serisinin tenant'taki sayacı (ADR-0049): seri ve dönem (yıl; sıfırlanmayan seride 0) başına tek satır, son verilen sıra numarasını tutar.
/// Satırı yalnızca numaralama servisi yazar (tek SQL ifadesiyle, satır kilidi altında); uygulama kodu doğrudan değiştirmez. Tenant verisidir:
/// Row-Level Security ile korunur, tenant dışa aktarımına ve silmesine girer. Seriler bugün şirketler arasında ortaktır (şirket başına seri sonraki dilim).
/// </summary>
public sealed class NumberCounter : ITenantEntity, ICompanyShared
{
    public const int SeriesKeyMaxLength = NumberSeriesDefinition.KeyMaxLength;

    private NumberCounter()
    {
        // EF Core için
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>Modülün beyan ettiği seri anahtarı (ör. <c>logistics.trip</c>).</summary>
    public string SeriesKey { get; private set; } = string.Empty;

    /// <summary>Yıllık seride yıl, sıfırlanmayan seride 0.</summary>
    public int Period { get; private set; }

    /// <summary>Bu dönemde verilen son sıra numarası.</summary>
    public long LastValue { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
