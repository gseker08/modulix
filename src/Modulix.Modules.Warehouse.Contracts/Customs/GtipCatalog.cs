namespace Modulix.Modules.Warehouse.Contracts.Customs;

/// <summary>İçe aktarılacak tarife satırı (doğrulanmış ve normalleştirilmiş).</summary>
public sealed record GtipCatalogEntryInput(string Code, string Description, string? Unit);

/// <summary>Katalog sürümü için gelen alanlar.</summary>
public sealed record GtipCatalogVersionInput(string Name, DateOnly EffectiveFrom, string? SourceFileName);

public sealed record GtipCatalogVersionView(Guid Id, string Name, DateOnly EffectiveFrom, string? SourceFileName, int EntryCount, DateTimeOffset CreatedAt);

public sealed record GtipCatalogEntryView(string Code, string Description, string? Unit);

public sealed record GtipCatalogEntryPage(IReadOnlyList<GtipCatalogEntryView> Items, int TotalCount, int Page, int PageSize);

/// <summary>Katalog kaydı bulunamadı (404).</summary>
public sealed class GtipCatalogNotFoundException(string message) : Exception(message);

/// <summary>Aynı yürürlük tarihli sürüm gibi veri durumundan doğan hata (409).</summary>
public sealed class GtipCatalogConflictException(string message) : Exception(message);

/// <summary>
/// Platform yöneticisinin GTİP kataloğu yönetimi. Yazmalar, değiştiren yönetici ile birlikte platform denetim kaydına aynı işlemde yazılır.
/// Satırlar bu arayüze ayrıştırılmış gelir; dosya biçimleri (Excel, CSV) ayrı bir okuyucudadır.
/// </summary>
public interface IGtipCatalogAdmin
{
    Task<IReadOnlyList<GtipCatalogVersionView>> ListVersionsAsync(CancellationToken cancellationToken);

    /// <summary>Yeni sürüm oluşturur. Satırlar geçersizse ya da yürürlük tarihinde sürüm varsa hata verir; hiçbir şey yazılmaz.</summary>
    Task<GtipCatalogVersionView> CreateVersionAsync(Guid actingUserId, GtipCatalogVersionInput input, IReadOnlyList<GtipCatalogEntryInput> entries, CancellationToken cancellationToken);

    Task DeleteVersionAsync(Guid actingUserId, Guid versionId, CancellationToken cancellationToken);

    /// <summary>
    /// Sürümdeki satırlar; <paramref name="search"/> kod ön eki ya da tanım içinde arar. <paramref name="sort"/>
    /// <see cref="GtipEntrySort.Keys"/> içinden bir anahtardır; boşsa koda göre artan sıralanır.
    /// </summary>
    Task<GtipCatalogEntryPage> SearchEntriesAsync(Guid versionId, string? search, int page, int pageSize, CancellationToken cancellationToken, string? sort = null);
}

/// <summary>Katalog sürümü ve satırları için saf kurallar (veritabanı gerekmez).</summary>
public static class GtipCatalogRules
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;
    public const int UnitMaxLength = 20;
    public const int FileNameMaxLength = 260;

    /// <summary>Tek sürümdeki en çok satır; Bakanlık cetveli bunun çok altındadır, sınır kötüye kullanımı keser.</summary>
    public const int MaxEntries = 200_000;

    /// <summary>
    /// Satırı doğrular ve normalleştirir: kod <see cref="GtipRules"/> ile, tanım zorunlu ve en çok <see cref="DescriptionMaxLength"/>,
    /// birim en çok <see cref="UnitMaxLength"/> karakterdir. Geçersizse hata iletisi döner.
    /// </summary>
    public static (GtipCatalogEntryInput? Entry, string? Error) Validate(string? code, string? description, string? unit)
    {
        var (value, codeError) = GtipRules.Normalize(code);
        if (codeError is not null)
        {
            return (null, codeError);
        }

        if (value is null)
        {
            return (null, "GTİP kodu boş olamaz.");
        }

        var text = description?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return (null, "Eşya tanımı boş olamaz.");
        }

        if (text.Length > DescriptionMaxLength)
        {
            return (null, $"Eşya tanımı en çok {DescriptionMaxLength} karakter olabilir.");
        }

        var unitText = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
        return unitText is { Length: > UnitMaxLength }
            ? (null, $"Birim en çok {UnitMaxLength} karakter olabilir.")
            : (new GtipCatalogEntryInput(value, text, unitText), null);
    }

    /// <summary>Satır listesi için toplu denetim: boş liste, fazla satır ve yinelenen kod hata verir (ilk hata iletisi döner).</summary>
    public static string? ValidateEntries(IReadOnlyList<GtipCatalogEntryInput> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return "Dosyada içe aktarılacak GTİP satırı yok.";
        }

        if (entries.Count > MaxEntries)
        {
            return $"Bir sürümde en çok {MaxEntries} satır olabilir.";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!seen.Add(entry.Code))
            {
                return $"Yinelenen GTİP kodu: {entry.Code}.";
            }
        }

        return null;
    }
}

/// <summary>GTİP satır listesinin başlığa göre sıralama anahtarları (ADR-0036).</summary>
public static class GtipEntrySort
{
    public static readonly IReadOnlyCollection<string> Keys = ["code", "-code", "description", "-description", "unit", "-unit"];
}
