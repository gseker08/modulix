namespace Modulix.Core.Contracts.Customization;

/// <summary>
/// Aktif tenant'ın ticari içerik işaretli özel alanları (ADR-0019 kilit kararı): değeri fiyatı ya da şartları belirlediği için sunulmuş
/// teklifte yerinde değişmeyen alanlar. Modüller tanım tablosunu okumaz; bu sınırdan sorar. İstek boyunca saklanabilir.
/// </summary>
public interface ICommercialCustomFields
{
    /// <summary>Türün arşivlenmemiş ve ticari işaretli alanlarının anahtarları (alan anahtarı, "custom." öneki olmadan).</summary>
    Task<IReadOnlySet<string>> GetKeysAsync(string entityKey, CancellationToken cancellationToken);
}
