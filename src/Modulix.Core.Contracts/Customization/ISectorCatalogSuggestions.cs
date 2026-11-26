namespace Modulix.Core.Contracts.Customization;

/// <summary>
/// Sektör profilinin önerdiği katalog kalemi (ör. lojistik profilinde masraf kalemleri). <see cref="Kind"/> modülün katalog türüdür
/// (CRM: Product | Service | Expense); Core türün anlamını bilmez, yalnızca taşır.
/// </summary>
public sealed record CatalogSuggestion(string Code, string Name, string Kind, string? Description = null);

/// <summary>
/// Aktif tenant'ın en son uyguladığı sektör profilinin önerdiği katalog kalemleri. Profil uygulanmamışsa ya da kalem önermiyorsa boştur.
/// Modül kalemleri kendi kayıt boru hattıyla (izin, denetim) yükler; Core katalog tablosuna dokunmaz.
/// </summary>
public interface ISectorCatalogSuggestions
{
    Task<IReadOnlyList<CatalogSuggestion>> ListAsync(CancellationToken cancellationToken);
}
