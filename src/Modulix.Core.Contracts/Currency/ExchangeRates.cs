namespace Modulix.Core.Contracts.Currency;

/// <summary>Çekirdeğin kur kayıt türleri (ADR-0049 dilim 5a).</summary>
public static class CurrencyEntities
{
    /// <summary>Firmanın döviz kurları: <c>/api/records/core.exchange-rate</c>.</summary>
    public const string ExchangeRate = "core.exchange-rate";
}

/// <summary>Kurun geldiği yer (ADR-0049 dilim 5, K5.2): dilim 5 yalnızca elle girişi taşır; merkez bankası çekimi ayrı karar isteyen 5d dilimidir.</summary>
public static class ExchangeRateSources
{
    public const string Manual = "Manual";

    public const string CentralBank = "CentralBank";

    public const string Import = "Import";

    public static IReadOnlyList<string> All { get; } = [Manual, CentralBank, Import];
}

/// <summary>
/// Bir gün için bulunan kur. <see cref="Rate"/> istenen yönde 1 kaynak birimin hedef birim karşılığıdır; ters kurdan türetildiyse
/// (<see cref="Inverted"/>) 1 / kayıtlı kurdur. <see cref="RateDate"/> kurun geçerlilik gününü verir (istenen günden eski olabilir).
/// </summary>
public sealed record ExchangeRateQuote(decimal Rate, DateOnly RateDate, bool Inverted);

/// <summary>Kayıtlı bir kur satırı (tarihçe dahil); toplu çeviri yapan raporlar için.</summary>
public sealed record ExchangeRatePoint(string From, string To, decimal Rate, DateOnly ValidOn);

/// <summary>
/// Firmanın döviz kurlarını okur (ADR-0049 dilim 5a; ADR-0018 okuma kuralı): istenen güne kadarki en yeni <b>doğrudan</b> kur, yoksa ters kur,
/// hiçbiri yoksa <c>null</c>. Kur uydurulmaz, çapraz kur hesaplanmaz. İzin gerektirmez; aktif tenant'ın satırlarını görür (sorgu süzgeci ve RLS).
/// Belgeye uygulanan kur belgede saklanır; sonradan eklenen kur eski belgeyi değiştirmez.
/// </summary>
public interface IExchangeRateReader
{
    /// <summary>1 <paramref name="from"/> kaç <paramref name="to"/>; kodlar üç harfli ISO 4217, büyük harf.</summary>
    Task<ExchangeRateQuote?> FindAsync(string from, string to, DateOnly day, CancellationToken cancellationToken);

    /// <summary>Bütün kayıtlı kurlar (tarihçe dahil), sırasız.</summary>
    Task<IReadOnlyList<ExchangeRatePoint>> ListAsync(CancellationToken cancellationToken);
}
