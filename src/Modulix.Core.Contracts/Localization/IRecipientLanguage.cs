namespace Modulix.Core.Contracts.Localization;

/// <summary>
/// Bir dildeki metinler: sözlük + seçilmiş dil. E-posta gibi alıcıya giden içerik, isteği yapanın değil alıcının dilinde bununla
/// üretilir (ADR-0034 §6).
/// </summary>
public sealed record LocalizedTexts(string Language, ITextCatalog Catalog)
{
    /// <summary>Anahtarın bu dildeki metni; <paramref name="args"/> yer tutuculara işlenir.</summary>
    public string Get(string key, params object?[] args) => Catalog.Get(Language, key, args);

    /// <summary>Bu dilin bütün metinleri (metadata adlarının çevirisi için, <see cref="MetadataTexts"/>).</summary>
    public IReadOnlyDictionary<string, string> Bundle => Catalog.Bundle(Language);
}

/// <summary>
/// Alıcının dili (ADR-0034 §6, dilim 18b). İsteğin dili (<see cref="IRequestLanguage"/>) işi yapan kişinindir; e-posta ise alıcının
/// diliyle yazılır: kullanıcı alıcıda kişinin tercihi → aktif tenant'ın varsayılanı (<c>core.regional.language</c>) → <c>tr</c>;
/// kullanıcı olmayan alıcıda (müşteri, dış adres) tenant varsayılanı → <c>tr</c>. Tenant varsayılanı yalnızca aktif tenant'tan okunur.
/// </summary>
public interface IRecipientLanguage
{
    /// <summary>Kullanıcı hesabı olan alıcının dili (tercihi yoksa tenant varsayılanı, o da yoksa <c>tr</c>).</summary>
    ValueTask<LocalizedTexts> ForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Kullanıcı hesabı olmayan alıcının dili: aktif tenant'ın varsayılanı, yoksa <c>tr</c>.</summary>
    ValueTask<LocalizedTexts> ForExternalAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Kayıttaki kişinin (müşteri yetkilisi) dili (dilim 22b): ortak kişinin yazışma dili → firmanın (verilen taraf, yoksa kişinin
    /// çalıştığı taraf) yazışma dili → <see cref="ForExternalAsync"/>. Kayıtlar aktif tenant'ta aranır; bulunamayan kimlik atlanır.
    /// </summary>
    ValueTask<LocalizedTexts> ForContactAsync(Guid? personId, Guid? partyId, CancellationToken cancellationToken);
}
