using Modulix.Core.Contracts.Numbering;

namespace Modulix.Core.Numbering;

/// <summary>İş kuralı ihlali (422).</summary>
public sealed class NumberSeriesException(string message) : Exception(message);

/// <summary>Eşzamanlı değişiklik (409).</summary>
public sealed class NumberSeriesConflictException(string message) : Exception(message);

/// <summary>
/// Firmanın gördüğü haliyle bir seri: beyan edilen varsayılan, uygulanan yapılandırma, içinde bulunulan dönemin son sırası ve sıradaki numara.
/// <see cref="Version"/> 0 ise firma seriye henüz dokunmamıştır.
/// </summary>
public sealed record NumberSeriesView(
    string ModuleKey, NumberSeriesDefinition Declared, NumberSeriesDefinition Effective, bool IsCustomized, int Year, long LastValue, string NextNumber, long Version);

/// <summary>
/// Belge serilerinin firma yönetimi (ADR-0049 dilim 2a): listeleme, önek/basamak/sıfırlama yapılandırması ve sayacı ileri alma. Yetki uç noktadadır
/// (<c>core.numbering.manage</c>); her değişiklik denetim kaydına yazılır.
/// </summary>
public interface INumberSeriesAdmin
{
    Task<IReadOnlyList<NumberSeriesView>> ListAsync(CancellationToken cancellationToken);

    /// <returns>Seri beyan edilmemişse null.</returns>
    Task<NumberSeriesView?> ConfigureAsync(
        string seriesKey, string? prefix, int digits, NumberSeriesReset reset, long expectedVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Bu dönemde verilecek sıradaki sıra numarasını ileri alır (yalnızca ileri; geri almak verilmiş numarayı yineler). Sayaç bu dönemde henüz
    /// açılmamışsa reddedilir: modülün eski numaralarından devam eden tohum ilk numarada sorulur, ondan önce ileri almak çakışma doğurabilir.
    /// </summary>
    /// <returns>Seri beyan edilmemişse null.</returns>
    Task<NumberSeriesView?> AdvanceAsync(string seriesKey, long nextValue, long expectedVersion, CancellationToken cancellationToken);
}
