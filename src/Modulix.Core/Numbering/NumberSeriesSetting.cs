using Modulix.Core.Contracts.Numbering;
using Modulix.Core.Contracts.Tenancy;

namespace Modulix.Core.Numbering;

/// <summary>
/// Firmanın bir belge serisi için yaptığı yapılandırma (ADR-0049 dilim 2a): önek, basamak sayısı ve sıfırlama. Boş alan modülün beyan ettiği
/// varsayılanı kullanır; varsayılana eşit değer boş saklanır. Seri anahtarı başına tek satır. Sayaç (<see cref="NumberCounter"/>) seri ve dönem
/// başına ayrı tutulduğu için önek ya da basamak değişikliği numara çakışması doğurmaz: sayaç kaldığı yerden devam eder, sıfırlama değişince yeni
/// dönemin sayacı açılır. Tenant verisidir: Row-Level Security ile korunur, tenant dışa aktarımına ve silmesine girer.
/// </summary>
public sealed class NumberSeriesSetting : ITenantEntity, ICompanyShared
{
    private NumberSeriesSetting()
    {
        // EF Core için
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string SeriesKey { get; private set; } = string.Empty;

    public string? Prefix { get; private set; }

    public int? Digits { get; private set; }

    public NumberSeriesReset? Reset { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid UpdatedBy { get; private set; }

    /// <summary>Eşzamanlılık belirteci: yapılandırma ve sayacı ileri alma her seferinde bir artırır.</summary>
    public long Version { get; private set; }

    public bool IsCustomized => Prefix is not null || Digits is not null || Reset is not null;

    public static NumberSeriesSetting Create(string seriesKey, Guid userId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        SeriesKey = seriesKey,
        UpdatedAt = now,
        UpdatedBy = userId,
    };

    /// <summary>Firmanın yapılandırması uygulanmış seri; yapılandırma yoksa beyan edilen varsayılan.</summary>
    public NumberSeriesDefinition Apply(NumberSeriesDefinition declared) =>
        declared with { Prefix = Prefix ?? declared.Prefix, Digits = Digits ?? declared.Digits, Reset = Reset ?? declared.Reset };

    /// <summary>Yapılandırmayı değiştirir. Önek büyük harfe çevrilir; geçersiz değer <see cref="ArgumentException"/> verir.</summary>
    public void Configure(NumberSeriesDefinition declared, string? prefix, int digits, NumberSeriesReset reset, Guid userId, DateTimeOffset now)
    {
        string code = (prefix ?? string.Empty).Trim().ToUpperInvariant();
        NumberSeriesDefinition candidate = declared with { Prefix = code, Digits = digits, Reset = reset };
        if (!candidate.IsValid)
        {
            throw new ArgumentException(
                $"Önek 1-{NumberSeriesDefinition.PrefixMaxLength} büyük harf ya da rakam, basamak sayısı {NumberSeriesDefinition.MinDigits}-{NumberSeriesDefinition.MaxDigits} olmalı.");
        }

        Prefix = code == declared.Prefix ? null : code;
        Digits = digits == declared.Digits ? null : digits;
        Reset = reset == declared.Reset ? null : reset;
        Touch(userId, now);
    }

    /// <summary>Sayaç ileri alındı: kim ve ne zaman; aynı anda yapılan yapılandırma değişikliği çakışma alır.</summary>
    public void Touch(Guid userId, DateTimeOffset now)
    {
        UpdatedAt = now;
        UpdatedBy = userId;
        Version++;
    }
}
