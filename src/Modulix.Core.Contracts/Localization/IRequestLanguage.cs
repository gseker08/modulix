namespace Modulix.Core.Contracts.Localization;

/// <summary>
/// İsteğin (ya da arka plan işinin) dili (ADR-0034 §2, §6): oturumdaki kullanıcının tercihi → aktif tenant'ın varsayılanı
/// (<c>core.regional.language</c>) → <c>tr</c>. Dil oturumdan belirlenir; <c>Accept-Language</c> başlığı kullanılmaz. Kullanıcısı
/// olmayan arka plan işinde tenant varsayılanı, tenant'ı da yoksa <c>tr</c> geçerlidir. İstek başına bir kez çözülür.
/// </summary>
public interface IRequestLanguage
{
    ValueTask<string> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// İsteğin dilinde metin: <see cref="ITextCatalog"/> + <see cref="IRequestLanguage"/>. Sunucu metinlerini kaynağında çevirmesi
/// gereken kod (ör. e-posta, rapor başlığı) bunu kullanır; API hata iletileri ise HTTP sınırında çevrilir.
/// </summary>
public interface ILocalizer
{
    ValueTask<string> GetLanguageAsync(CancellationToken cancellationToken);

    /// <summary>Anahtarın isteğin dilindeki metni; <paramref name="args"/> yer tutuculara işlenir.</summary>
    ValueTask<string> TextAsync(string key, CancellationToken cancellationToken, params object?[] args);

    /// <summary>İletinin isteğin dilindeki metni; anahtarsız ya da sözlükte bulunmayan iletide Türkçe kaynak metin.</summary>
    ValueTask<string> TextAsync(ServerMessage message, CancellationToken cancellationToken);
}
