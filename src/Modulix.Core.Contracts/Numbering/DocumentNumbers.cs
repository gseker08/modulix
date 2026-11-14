using System.Globalization;

namespace Modulix.Core.Contracts.Numbering;

/// <summary>
/// Ortak belge numaralama (ADR-0049, yol haritası Ç8.1): modül numarayı kendisi üretmez, beyan ettiği seriden ister. Sayaç tenant'a aittir
/// ve çağıranın açık işlemi içinde artar: aynı anda numara isteyen iki işlem aynı numarayı alamaz, işlem geri alınırsa numara da geri döner.
/// Açık işlem yokken alınan numara kayıt başarısız olsa bile harcanmış sayılır (seri boşluk verebilir).
/// </summary>
public interface IDocumentNumbers
{
    /// <param name="seriesKey">Modülün <c>IModule.NumberSeries</c> ile beyan ettiği seri; beyansız seri hata verir.</param>
    /// <param name="seedLastValue">
    /// Seri bu dönemde ilk kez kullanılıyorsa çağrılır ve o ana dek verilmiş en büyük sıra numarasını döndürür (parametre: dönemin yılı).
    /// Numarasını daha önce kendisi üreten kayıt türleri ortak seriye geçerken kaldıkları yerden devam etsin diye vardır; yeni seride verilmez.
    /// </param>
    Task<string> NextAsync(string seriesKey, Func<int, CancellationToken, Task<long>>? seedLastValue = null, CancellationToken cancellationToken = default);
}

/// <summary>Sayacın ne zaman 1'e döndüğü.</summary>
public enum NumberSeriesReset
{
    /// <summary>Hiç sıfırlanmaz; numara yıl taşımaz (ör. <c>MS-00042</c>).</summary>
    Never = 0,

    /// <summary>Her takvim yılında (tenant saat dilimine göre) 1'den başlar; numara yılı taşır (ör. <c>SF-2026-00042</c>).</summary>
    Yearly = 1,
}

/// <summary>
/// Bir belge serisi. <see cref="Key"/> modül anahtarıyla başlar (ör. <c>logistics.trip</c>) ve değişmez: sayaç bu anahtarla saklanır.
/// Önek ve basamak sayısı kodda tanımlı varsayılandır; firmanın değiştirebilmesi sonraki dilimdir (ADR-0049).
/// </summary>
public sealed record NumberSeriesDefinition(
    string Key,
    string DisplayName,
    string Prefix,
    int Digits = 5,
    NumberSeriesReset Reset = NumberSeriesReset.Yearly)
{
    public const int KeyMaxLength = 100;
    public const int PrefixMaxLength = 10;
    public const int MinDigits = 3;
    public const int MaxDigits = 10;

    /// <summary>Tanım geçerli mi? Önek yalnızca büyük harf ve rakamdan oluşur.</summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Key) && Key.Length <= KeyMaxLength
        && !string.IsNullOrWhiteSpace(DisplayName)
        && Prefix is { Length: > 0 and <= PrefixMaxLength } && Prefix.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
        && Digits is >= MinDigits and <= MaxDigits
        && Enum.IsDefined(Reset);

    /// <summary>Sayacın dönemi: yıllık seride yıl, sıfırlanmayan seride 0.</summary>
    public int PeriodOf(int year) => Reset == NumberSeriesReset.Yearly ? year : 0;

    /// <summary>Numaranın sıra numarasından önceki kısmı (ör. <c>SF-2026-</c>).</summary>
    public string PrefixOf(int year) =>
        Reset == NumberSeriesReset.Yearly ? $"{Prefix}-{year.ToString("D4", CultureInfo.InvariantCulture)}-" : $"{Prefix}-";

    /// <summary>Sıra numarasını belge numarasına çevirir; basamak sayısını aşan sıra kısaltılmaz.</summary>
    public string Format(long value, int year) => PrefixOf(year) + value.ToString("D" + Digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>Bu serinin ürettiği bir numaradan sıra numarasını okur; biçim uymuyorsa null.</summary>
    public long? ValueOf(string? number, int year)
    {
        var prefix = PrefixOf(year);
        return number is not null && number.StartsWith(prefix, StringComparison.Ordinal)
            && long.TryParse(number.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
    }
}
