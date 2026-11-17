using System.Text;

namespace Modulix.Core.Imports;

/// <summary>
/// CSV okuyucu. Türkçe Excel "CSV olarak kaydet" çoğunlukla noktalı virgülle ve Windows-1254 kodlamasıyla yazar; "CSV UTF-8" seçeneği
/// BOM'lu UTF-8'dir. Kodlama (UTF-8 değilse Windows-1254) ve ayraç (noktalı virgül, virgül, sekme) içerikten anlaşılır.
/// </summary>
public static class CsvFile
{
    /// <summary>Baytları metne çevirir: BOM'lu ya da geçerli UTF-8 olduğu gibi, değilse Windows-1254.</summary>
    public static string Decode(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var start = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF ? 3 : 0;
        try
        {
            return utf8.GetString(content, start, content.Length - start);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1254).GetString(content, start, content.Length - start);
        }
    }

    /// <summary>İlk dolu satırdaki ayraç sayılarına göre ayracı seçer; eşitlikte noktalı virgül (Türkçe Excel varsayılanı).</summary>
    public static char DetectDelimiter(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var line = text.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? string.Empty;
        char[] candidates = [';', ',', '\t'];
        return candidates.OrderByDescending(c => line.Count(x => x == c)).ThenBy(c => Array.IndexOf(candidates, c)).First();
    }

    /// <summary>RFC 4180 benzeri ayrıştırma: tırnaklı alan ayraç ve satır sonu taşıyabilir, "" bir tırnaktır. Boş hücre <c>null</c>.</summary>
    public static IEnumerable<IReadOnlyList<object?>> Parse(byte[] content)
    {
        var text = Decode(content);
        var delimiter = DetectDelimiter(text);
        var row = new List<object?>();
        var field = new StringBuilder();
        var quoted = false;
        var fieldStarted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"' && !fieldStarted)
            {
                quoted = true;
                fieldStarted = true;
            }
            else if (c == delimiter)
            {
                row.Add(Finish(field));
                fieldStarted = false;
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(Finish(field));
                fieldStarted = false;
                yield return row;
                row = [];
            }
            else
            {
                field.Append(c);
                fieldStarted = true;
            }
        }

        if (fieldStarted || field.Length > 0 || row.Count > 0)
        {
            row.Add(Finish(field));
            yield return row;
        }
    }

    private static string? Finish(StringBuilder field)
    {
        var value = field.ToString();
        field.Clear();
        return value.Length == 0 ? null : value;
    }
}
