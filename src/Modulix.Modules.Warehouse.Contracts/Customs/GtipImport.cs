using System.Globalization;
using System.Text;

namespace Modulix.Modules.Warehouse.Contracts.Customs;

/// <param name="Row">Dosyadaki satır sırası (1'den; boş satırlar sayılır).</param>
public sealed record GtipFileRowError(int Row, string Message);

/// <summary>
/// Dosya okuma sonucu. <see cref="Entries"/> yalnızca geçerli satırlardır; <see cref="ErrorCount"/> sıfırdan büyükse sürüm oluşturulmaz
/// (eksik katalog sessizce yüklenmesin). <see cref="Errors"/> ilk <see cref="GtipImportRules.MaxReportedErrors"/> hatayı taşır.
/// </summary>
/// <param name="HeadingRows">Fasıl/pozisyon başlığı gibi 6 haneden kısa kodlu satırlar; kataloğa alınmaz, hata sayılmaz.</param>
/// <param name="PaddedCodes">Sayı olarak kaydedildiği için baştaki sıfırı kaybetmiş, sıfırı tamamlanan kod sayısı.</param>
/// <param name="FatalError">Dosya hiç yorumlanamadıysa (biçim, başlık) iletisi.</param>
public sealed record GtipFileReadResult(
    IReadOnlyList<GtipCatalogEntryInput> Entries,
    IReadOnlyList<GtipFileRowError> Errors,
    int ErrorCount,
    int RowsRead,
    int HeadingRows,
    int PaddedCodes,
    string? FatalError)
{
    public bool CanImport => FatalError is null && ErrorCount == 0 && Entries.Count > 0;

    public static GtipFileReadResult Fatal(string message) => new([], [], 0, 0, 0, 0, message);
}

/// <summary>Excel (.xlsx) ve CSV dosyasından GTİP satırlarını okur; sürümü oluşturmaz.</summary>
public interface IGtipFileReader
{
    /// <summary>Dosyanın uzantısına göre biçimi seçer (.xlsx, .csv, .txt); okunamayan dosya <see cref="GtipFileReadResult.FatalError"/> döner.</summary>
    GtipFileReadResult Read(byte[] content, string? fileName);
}

/// <summary>
/// Dosya satırlarını yorumlayan saf kurallar: başlık satırını bulur, sütunları eşler, her satırı <see cref="GtipCatalogRules"/> ile
/// doğrular. Biçimden bağımsızdır (Excel ve CSV aynı hücre listesine iner), veritabanı gerekmez.
/// </summary>
public static class GtipImportRules
{
    public const int MaxFileBytes = 20 * 1024 * 1024;
    public const int MaxReportedErrors = 100;

    /// <summary>Başlık satırı bu satıra kadar aranır (cetvellerin üstünde başlık/açıklama satırları olabilir).</summary>
    public const int MaxHeaderSearchRows = 20;

    /// <summary>Okunacak en çok satır; sınır bozuk ya da kötü niyetli dosyanın belleği doldurmasını keser.</summary>
    public const int MaxRows = GtipCatalogRules.MaxEntries + 1000;

    private static readonly string[] CodeHeaders = ["gtip", "gtipkodu", "gtpkodu", "kod", "code", "hscode", "tarifekodu", "tarifepozisyonu", "gumruktarifeistatistikpozisyonu", "pozisyon"];
    private static readonly string[] DescriptionHeaders = ["tanim", "tanimi", "esyatanimi", "esyaninadi", "aciklama", "description", "esya", "adi", "ad"];
    private static readonly string[] UnitHeaders = ["birim", "olcubirimi", "ekbirim", "olcu", "unit"];

    public const string HeaderHelp = "GTİP kodu için 'GTİP' (ya da 'Kod'), eşya tanımı için 'Tanım' (ya da 'Eşya Tanımı', 'Açıklama') başlıklı sütunlar gerekir; 'Birim' sütunu isteğe bağlıdır.";

    /// <summary>Hücre satırlarını yorumlar. Her satır, sütun sırasına göre hücre değerleridir (boş hücre <c>null</c>).</summary>
    public static GtipFileReadResult Interpret(IEnumerable<IReadOnlyList<object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        (int Code, int Description, int Unit)? map = null;
        var entries = new List<GtipCatalogEntryInput>();
        var errors = new List<GtipFileRowError>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int rowNumber = 0, errorCount = 0, headings = 0, padded = 0, dataRows = 0;

        foreach (var row in rows)
        {
            rowNumber++;
            if (rowNumber > MaxRows)
            {
                return GtipFileReadResult.Fatal($"Dosyada en çok {MaxRows} satır okunabilir.");
            }

            if (map is null)
            {
                map = MapHeader(row);
                if (map is null && rowNumber >= MaxHeaderSearchRows)
                {
                    return GtipFileReadResult.Fatal($"İlk {MaxHeaderSearchRows} satırda başlık satırı bulunamadı. {HeaderHelp}");
                }

                continue;
            }

            var (codeCell, description, unit) = (Cell(row, map.Value.Code), Text(Cell(row, map.Value.Description)), Text(Cell(row, map.Value.Unit)));
            var codeText = Text(codeCell, out var wasNumber);
            if (codeText is null && description is null && unit is null)
            {
                continue; // boş satır
            }

            dataRows++;
            void Fail(string message)
            {
                errorCount++;
                if (errors.Count < MaxReportedErrors)
                {
                    errors.Add(new GtipFileRowError(rowNumber, message));
                }
            }

            if (codeCell is DateTime or DateTimeOffset or DateOnly)
            {
                Fail("GTİP kodu tarih olarak yorumlanmış; sütunu metin olarak biçimlendirip kodu yeniden girin.");
                continue;
            }

            if (codeText is null)
            {
                Fail("GTİP kodu boş.");
                continue;
            }

            var compact = Compact(codeText);

            // Fasıl (2) ve pozisyon (4) başlıkları kod değildir; kataloğa girmez, hata da sayılmaz.
            if (compact.Length is > 0 and < GtipRules.MinLength && compact.All(char.IsAsciiDigit))
            {
                headings++;
                continue;
            }

            // Sayı olarak saklanan kodlar baştaki sıfırı kaybeder (09023000 → 9023000); fasıl 01-99 olduğundan tek haneli fasıl
            // tek sayıda hane demektir ve sıfır tamamlanır.
            if (wasNumber && compact.Length % 2 == 1 && compact.Length < GtipRules.MaxLength && compact.All(char.IsAsciiDigit))
            {
                compact = "0" + compact;
                padded++;
            }

            var (entry, error) = GtipCatalogRules.Validate(compact, description, unit);
            if (entry is null)
            {
                Fail(error!);
                continue;
            }

            if (!seen.Add(entry.Code))
            {
                Fail($"Yinelenen GTİP kodu: {entry.Code}.");
                continue;
            }

            entries.Add(entry);
        }

        return map is null
            ? GtipFileReadResult.Fatal($"Başlık satırı bulunamadı. {HeaderHelp}")
            : new GtipFileReadResult(entries, errors, errorCount, dataRows, headings, padded, null);
    }

    /// <summary>Başlık satırıysa sütun sıralarını verir: kod ve tanım zorunlu, birim yoksa <c>-1</c>.</summary>
    public static (int Code, int Description, int Unit)? MapHeader(IReadOnlyList<object?> row)
    {
        int code = -1, description = -1, unit = -1;
        for (var i = 0; i < row.Count; i++)
        {
            var name = Fold(Text(row[i]) ?? string.Empty);
            if (code < 0 && CodeHeaders.Contains(name))
            {
                code = i;
            }
            else if (description < 0 && DescriptionHeaders.Contains(name))
            {
                description = i;
            }
            else if (unit < 0 && UnitHeaders.Contains(name))
            {
                unit = i;
            }
        }

        return code >= 0 && description >= 0 ? (code, description, unit) : null;
    }

    /// <summary>Başlık eşlemesi için: küçük harf, Türkçe harfler sadeleşir, harf/rakam dışı her şey atılır ("Eşya Tanımı" → "esyatanimi").</summary>
    public static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var folded = c switch
            {
                'İ' or 'I' or 'ı' or 'i' => 'i',
                'Ş' or 'ş' => 's',
                'Ç' or 'ç' => 'c',
                'Ğ' or 'ğ' => 'g',
                'Ö' or 'ö' => 'o',
                'Ü' or 'ü' => 'u',
                _ => char.ToLowerInvariant(c),
            };
            if (char.IsAsciiLetterOrDigit(folded))
            {
                sb.Append(folded);
            }
        }

        return sb.ToString();
    }

    private static string Compact(string code) => new([.. code.Where(c => c is not ('.' or ' ' or ' '))]);

    private static object? Cell(IReadOnlyList<object?> row, int index) => index >= 0 && index < row.Count ? row[index] : null;

    private static string? Text(object? cell) => Text(cell, out _);

    /// <summary>Hücreyi metne çevirir; tam sayı değerleri ondalıksız yazılır (<c>wasNumber</c>: hücre sayıydı). Boşsa null.</summary>
    private static string? Text(object? cell, out bool wasNumber)
    {
        wasNumber = false;
        switch (cell)
        {
            case null:
                return null;
            case string s:
                return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            case bool or DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan:
                return Convert.ToString(cell, CultureInfo.InvariantCulture);
            case IConvertible convertible when cell is not char:
                wasNumber = true;
                var number = convertible.ToDecimal(CultureInfo.InvariantCulture);
                return decimal.Truncate(number) == number
                    ? number.ToString("F0", CultureInfo.InvariantCulture)
                    : number.ToString(CultureInfo.InvariantCulture);
            default:
                var text = Convert.ToString(cell, CultureInfo.InvariantCulture);
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
    }
}
