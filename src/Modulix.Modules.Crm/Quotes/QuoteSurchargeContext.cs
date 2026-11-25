using System.Text;
using System.Text.Json.Nodes;
using Modulix.Core.Contracts.Pricing;
using Modulix.Modules.Crm.Domain;

namespace Modulix.Modules.Crm.Quotes;

/// <summary>
/// Teklifi ek ücret motorunun bağlamına çeviren saf kurucu (ADR-0018 devamı, Dilim 2c). Veritabanı gerektirmez. Anahtarlar küçük harf "slug"tır:
/// <c>quote.subtotal</c> (ana toplama giren kalemlerin belge indirimi öncesi net tutarı), <c>group.&lt;grup&gt;</c> (kalem <c>GroupName</c>'i),
/// <c>line.&lt;ürün kodu&gt;</c> (o koddaki kalemlerin tutarı), <c>quantity.&lt;ürün kodu&gt;</c> (miktar) ve teklifin özel alanları (kendi anahtarıyla).
/// Kalem tutarı <c>ListAmount − LineDiscount</c>'tır; seçilmemiş isteğe bağlı/alternatif kalem (<c>Included=false</c>) hiçbir toplama girmez.
/// Kuraldan üretilmiş satırlar (<c>SourceRuleId</c> dolu) bağlama girmez: ek ücret kendi tabanını beslemez.
/// </summary>
public static class QuoteSurchargeContextBuilder
{
    /// <summary>Metni anahtar biçimine çevirir: küçük harf, Türkçe harfler ASCII'ye, harf-rakam dışı her dizi tek tire, uçlardaki tireler atılır.</summary>
    public static string Slug(string? text)
    {
        var builder = new StringBuilder();
        foreach (char c in (text ?? string.Empty).Trim())
        {
            char mapped = c switch
            {
                'ç' or 'Ç' => 'c', 'ğ' or 'Ğ' => 'g', 'ı' or 'İ' or 'I' => 'i', 'ö' or 'Ö' => 'o', 'ş' or 'Ş' => 's', 'ü' or 'Ü' => 'u',
                _ => char.ToLowerInvariant(c),
            };
            if (char.IsAsciiLetterOrDigit(mapped))
            {
                builder.Append(mapped);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().TrimEnd('-');
    }

    internal static SurchargeContext Build(string currency, IEnumerable<QuoteLine> lines, JsonObject? customValues)
    {
        var numbers = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);

        void Add(string key, decimal amount) => numbers[key] = numbers.GetValueOrDefault(key) + amount;

        decimal subtotal = 0m;
        foreach (QuoteLine line in lines.Where(l => l.Included && l.SourceRuleId is null))
        {
            decimal amount = line.ListAmount - line.LineDiscount;
            subtotal += amount;
            if (Slug(line.GroupName) is { Length: > 0 } group)
            {
                Add("group." + group, amount);
            }

            if (Slug(line.ProductCode) is { Length: > 0 } code)
            {
                Add("line." + code, amount);
                Add("quantity." + code, line.Quantity);
            }
        }

        numbers["quote.subtotal"] = subtotal;

        foreach (var (key, node) in customValues ?? [])
        {
            // Özel alan anahtarı yerleşik anahtarların (quote., group., line., quantity.) üzerine yazamaz; noktalı özel alan anahtarı zaten üretilmez.
            if (node is not JsonValue value || numbers.ContainsKey(key) || texts.ContainsKey(key))
            {
                continue;
            }

            if (value.TryGetValue<bool>(out var flag))
            {
                texts[key] = flag ? "true" : "false";
            }
            else if (TryNumber(value, out var number))
            {
                numbers[key] = number;
            }
            else if (value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                texts[key] = text.Trim();
            }
        }

        return new SurchargeContext(currency, numbers, texts);
    }

    // JSON'dan gelen sayı decimal olarak okunur; kodla kurulan (int/long/double) düğümler de kabul edilir.
    private static bool TryNumber(JsonValue value, out decimal number)
    {
        if (value.TryGetValue(out number))
        {
            return true;
        }

        if (value.TryGetValue<long>(out var whole))
        {
            number = whole;
            return true;
        }

        if (value.TryGetValue<int>(out var small))
        {
            number = small;
            return true;
        }

        if (value.TryGetValue<double>(out var real) && double.IsFinite(real))
        {
            number = (decimal)real;
            return true;
        }

        number = 0m;
        return false;
    }
}
