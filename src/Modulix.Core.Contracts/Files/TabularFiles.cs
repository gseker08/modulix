namespace Modulix.Core.Contracts.Files;

/// <summary>Tablo dosyası okunamadı (bozuk ya da desteklenmeyen biçim). İleti kullanıcıya gösterilir.</summary>
public sealed class TabularFileException(string message) : Exception(message);

/// <summary>
/// Excel (<c>.xlsx</c>, ilk sayfa) ve CSV (<c>.csv</c>/<c>.txt</c>) dosyasını hücre satırlarına indirir; yorumlama çağırandadır. Gerçeklemesi altyapıdadır,
/// modül dosya biçimi paketine bağımlı olmaz. Eski ikili <c>.xls</c> desteklenmez.
/// </summary>
public interface ITabularFileReader
{
    /// <summary>
    /// Biçimi uzantıdan seçer. Her satır sütun sırasına göre hücre değerleridir (boş hücre <c>null</c>; Excel'de sayı ve tarih kendi türüyle gelir).
    /// Satırlar sırayla okunur; desteklenmeyen ya da okunamayan dosyada, okuma sırasında da, <see cref="TabularFileException"/> fırlatır.
    /// </summary>
    IEnumerable<IReadOnlyList<object?>> ReadRows(byte[] content, string? fileName);
}
