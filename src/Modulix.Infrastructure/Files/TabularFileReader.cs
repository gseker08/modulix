using System.IO.Compression;
using MiniExcelLibs;
using Modulix.Core.Contracts.Files;
using Modulix.Core.Imports;

namespace Modulix.Infrastructure.Files;

/// <summary>
/// Tablo dosyası okuyucu (<see cref="ITabularFileReader"/>): <c>.xlsx</c> ilk sayfası MiniExcel ile satır satır, <c>.csv</c>/<c>.txt</c>
/// <see cref="CsvFile"/> ile (kodlama ve ayraç içerikten anlaşılır). Biçim hataları <see cref="TabularFileException"/>'a çevrilir.
/// </summary>
internal sealed class TabularFileReader : ITabularFileReader
{
    /// <summary>Açılmış (sıkıştırması çözülmüş) toplam boyut sınırı: küçük bir .xlsx'in belleği doldurmasını (zip bombası) keser.</summary>
    private const long MaxUncompressedBytes = 200L * 1024 * 1024;

    private const string Unreadable = "Dosya okunamadı; geçerli bir Excel (.xlsx) ya da CSV dosyası olduğundan emin olun.";

    public IEnumerable<IReadOnlyList<object?>> ReadRows(byte[] content, string? fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".xlsx" => Guard(Workbook(content)),
            ".csv" or ".txt" => Guard(CsvFile.Parse(content)),
            ".xls" => throw new TabularFileException("Eski .xls biçimi desteklenmiyor; dosyayı Excel'de '.xlsx' ya da 'CSV' olarak kaydedip yükleyin."),
            _ => throw new TabularFileException("Desteklenen dosya biçimleri: .xlsx ve .csv."),
        };
    }

    private static IEnumerable<IReadOnlyList<object?>> Workbook(byte[] content)
    {
        // .xlsx bir zip dosyasıdır ("PK" ile başlar); metin dosyasının uzantısı değiştirilmişse anlaşılır bir ileti verilir.
        if (content.Length < 4 || content[0] != 'P' || content[1] != 'K')
        {
            throw new TabularFileException("Dosya geçerli bir .xlsx değil (Excel çalışma kitabı olarak kaydedilmemiş).");
        }

        using var stream = new MemoryStream(content, writable: false);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            if (zip.Entries.Sum(e => e.Length) > MaxUncompressedBytes)
            {
                throw new TabularFileException("Excel dosyasının açılmış boyutu çok büyük; dosyayı küçültüp yeniden yükleyin.");
            }
        }

        stream.Position = 0;
        foreach (var row in MiniExcel.Query(stream, useHeaderRow: false, excelType: ExcelType.XLSX))
        {
            yield return ToCells(row);
        }
    }

    /// <summary>Okuma sırasında çıkan biçim hatalarını kullanıcıya gösterilecek <see cref="TabularFileException"/>'a çevirir.</summary>
    private static IEnumerable<IReadOnlyList<object?>> Guard(IEnumerable<IReadOnlyList<object?>> rows)
    {
        using var enumerator = rows.GetEnumerator();
        while (true)
        {
            bool more;
            try
            {
                more = enumerator.MoveNext();
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or FormatException or OverflowException
                or System.Xml.XmlException or IOException or KeyNotFoundException)
            {
                throw new TabularFileException(Unreadable);
            }

            if (!more)
            {
                yield break;
            }

            yield return enumerator.Current;
        }
    }

    /// <summary>MiniExcel satırı sütun harfiyle (A, B, … AA) anahtarlı sözlüktür; sütun sırasına göre hücre listesine çevrilir.</summary>
    private static IReadOnlyList<object?> ToCells(object row)
    {
        if (row is not IDictionary<string, object?> cells || cells.Count == 0)
        {
            return [];
        }

        var list = new object?[cells.Keys.Max(ColumnIndex) + 1];
        foreach (var (column, value) in cells)
        {
            list[ColumnIndex(column)] = value;
        }

        return list;
    }

    /// <summary>"A" → 0, "B" → 1, "AA" → 26.</summary>
    private static int ColumnIndex(string letters)
    {
        var index = 0;
        foreach (var c in letters.ToUpperInvariant())
        {
            if (c is < 'A' or > 'Z')
            {
                throw new FormatException($"Geçersiz sütun adı: {letters}");
            }

            index = (index * 26) + (c - 'A' + 1);
        }

        return index - 1;
    }
}
