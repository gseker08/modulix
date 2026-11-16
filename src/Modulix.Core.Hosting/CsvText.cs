namespace Modulix.Core.Hosting;

/// <summary>CSV hücre yazımı; liste dışa aktarımlarında ortak (ADR-0046).</summary>
public static class CsvText
{
    /// <summary>
    /// Hücre metni. CSV enjeksiyonuna karşı formül başlatan karakterlerle (<c>= + - @</c>, sekme, satır başı) başlayan değerin başına kesme konur;
    /// virgül, tırnak ya da satır sonu içeren değer tırnağa alınır.
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
