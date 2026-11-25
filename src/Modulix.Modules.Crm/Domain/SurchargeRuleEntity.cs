using Modulix.Core.Contracts.Pricing;
using Modulix.Modules.Crm.Pricing;

namespace Modulix.Modules.Crm.Domain;

/// <summary>
/// Ek ücret kuralı (ADR-0018 devamı, Dilim 2b): teklif hesabına koşullu ek ücret, asgari bedel ve süre aşımı ekleyen, firmanın yönettiği kural.
/// Kural verisidir; kodda sektör ya da müşteri adı yoktur. Hesabı <see cref="SurchargeEngine"/> yapar; bu varlık yalnızca onun girdisini saklar.
/// Anahtarlar (<see cref="QuantityKeys"/>, <see cref="BaseKey"/>, <see cref="DurationKey"/>, koşul anahtarları) teklifteki ya da satırdaki alanların
/// adlarıdır (özel alan anahtarları dahil). Etkin kural fiyat okuma/yazma izinleriyle yönetilir; değişiklik sürümü artırır.
/// </summary>
public sealed class SurchargeRule : CrmEntity
{
    public const int NameMaxLength = 150;
    public const int DescriptionMaxLength = 500;
    public const int GroupMaxLength = 60;
    public const int KeyMaxLength = 100;
    public const int ListMaxLength = 2000;

    public string Name { get; internal set; } = string.Empty;

    public string? Description { get; internal set; }

    /// <summary>Küçük sayı önce hesaplanır (asgari bedel kuralları her zaman diğerlerinden sonra).</summary>
    public int Priority { get; internal set; }

    public bool IsActive { get; internal set; } = true;

    /// <summary><see cref="SurchargeRules.Kinds"/>.</summary>
    public string Kind { get; internal set; } = SurchargeRules.Fixed;

    /// <summary>Kural yalnızca bu para birimindeki tekliflere uygulanır (tutar para birimine bağlıdır).</summary>
    public string Currency { get; internal set; } = "TRY";

    /// <summary>Sabit tutar, birim ücreti ya da asgari bedel.</summary>
    public decimal? Amount { get; internal set; }

    /// <summary>Yüzde (0–100): bir tabanın yüzdesi ya da süre aşımında birim ücretinin tabanın yüzdesi olması.</summary>
    public decimal? Percent { get; internal set; }

    /// <summary>Kuralın katkıda bulunduğu toplam grubu ("ardiye" gibi); asgari bedel kendi grubunu denetler.</summary>
    public string? GroupKey { get; internal set; }

    public string? BaseKey { get; internal set; }

    public string? DurationKey { get; internal set; }

    /// <summary>Süre aşımında serbest birim sayısı (ör. ilk 7 gün).</summary>
    public decimal FreeUnits { get; internal set; }

    /// <summary>JSON metin dizisi: çarpan anahtarları (ör. beyanname adedi, saat, kişi).</summary>
    public string? QuantityKeys { get; internal set; }

    /// <summary>JSON dizisi: <c>[{ "key", "operator", "value" }]</c>; tümü sağlanırsa kural çalışır.</summary>
    public string? Conditions { get; internal set; }

    /// <summary><see cref="SurchargeRules.Presentations"/>: teklifte ayrı satır, gruba dahil ya da yalnız hesap dökümü.</summary>
    public string Presentation { get; internal set; } = SurchargeRules.SeparateLine;

    /// <summary>Hayırsa kural çalıştığında aynı gruptaki sonraki kurallar uygulanmaz.</summary>
    public bool Stackable { get; internal set; } = true;
}
